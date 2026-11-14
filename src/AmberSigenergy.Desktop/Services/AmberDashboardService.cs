using System.Globalization;
using System.Text.Json;
using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop.Services;

public sealed class AmberDashboardService
{
    private readonly AmberApiClient _api;

    public AmberDashboardService(AmberApiClient api) => _api = api;

    public async Task<AmberDashboardSnapshot> LoadTodayAsync(string? configuredSiteId, CancellationToken ct = default)
    {
        using var sitesDoc = await _api.GetSitesAsync(ct);
        if (sitesDoc.RootElement.ValueKind != JsonValueKind.Array || sitesDoc.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Amber returned no electricity sites.");

        JsonElement site = default;
        if (!string.IsNullOrWhiteSpace(configuredSiteId))
        {
            site = sitesDoc.RootElement.EnumerateArray()
                .FirstOrDefault(x => StringEquals(x, "id", configuredSiteId));
            if (site.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("The SiteId in appsettings.json was not found in the Amber account.");
        }
        else if (sitesDoc.RootElement.GetArrayLength() == 1)
        {
            site = sitesDoc.RootElement[0];
        }
        else
        {
            throw new InvalidOperationException("More than one Amber site was found. Set Amber.SiteId in appsettings.json to the desired site's id.");
        }

        var siteId = GetString(site, "id") ?? throw new InvalidOperationException("Amber site response did not include an id.");
        var network = GetString(site, "network") ?? "Unknown network";
        var intervalMinutes = site.TryGetProperty("intervalLength", out var interval) && interval.TryGetInt32(out var minutes) ? minutes : 30;

        var channelTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (site.TryGetProperty("channels", out var channels) && channels.ValueKind == JsonValueKind.Array)
        {
            foreach (var channel in channels.EnumerateArray())
            {
                var identifier = GetString(channel, "identifier");
                var type = GetString(channel, "type");
                if (!string.IsNullOrWhiteSpace(identifier) && !string.IsNullOrWhiteSpace(type))
                    channelTypes[identifier] = type;
            }
        }

        var generalChannel = channelTypes.FirstOrDefault(x => x.Value.Equals("general", StringComparison.OrdinalIgnoreCase)).Key;
        var feedInChannel = channelTypes.FirstOrDefault(x => x.Value.Equals("feedIn", StringComparison.OrdinalIgnoreCase)).Key;
        if (string.IsNullOrWhiteSpace(generalChannel) || string.IsNullOrWhiteSpace(feedInChannel))
            throw new InvalidOperationException("Amber site must have both a general import channel and a feedIn export channel.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        using var usageDoc = await _api.GetUsageAsync(siteId, today, today, ct);
        using var pricesDoc = await _api.GetPricesAsync(siteId, today, today, intervalMinutes == 5 ? 5 : 30, ct);
        using var currentDoc = await _api.GetCurrentPricesAsync(siteId, 2, 2, ct);

        var priceMap = new Dictionary<(long UtcTicks, string ChannelType), (decimal Price, bool Actual)>();
        if (pricesDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var price in pricesDoc.RootElement.EnumerateArray())
            {
                var type = GetString(price, "channelType");
                var start = GetDateTimeOffset(price, "startTime");
                if (type is null || start is null || !price.TryGetProperty("perKwh", out var perKwh) || !perKwh.TryGetDecimal(out var cents))
                    continue;
                var recordType = GetString(price, "type") ?? "";
                var isActual = recordType.Equals("ActualInterval", StringComparison.OrdinalIgnoreCase);
                if (!isActual && !recordType.Equals("CurrentInterval", StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = (start.Value.UtcTicks, type);
                if (!priceMap.TryGetValue(key, out var old) || (isActual && !old.Actual))
                    priceMap[key] = (cents, isActual);
            }
        }

        decimal? currentImport = null;
        decimal? currentExport = null;
        var currentImportRank = -1;
        var currentExportRank = -1;
        if (currentDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var price in currentDoc.RootElement.EnumerateArray())
            {
                var channelType = GetString(price, "channelType");
                var recordType = GetString(price, "type");
                if (!price.TryGetProperty("perKwh", out var value) || !value.TryGetDecimal(out var cents))
                    continue;

                // Do not let future forecast rows overwrite the live/current price.
                var rank = recordType == "CurrentInterval" ? 2 : recordType == "ActualInterval" ? 1 : 0;
                if (rank == 0) continue;
                if (channelType == "general" && rank >= currentImportRank)
                {
                    currentImport = cents;
                    currentImportRank = rank;
                }
                if (channelType == "feedIn" && rank >= currentExportRank)
                {
                    currentExport = cents;
                    currentExportRank = rank;
                }
            }
        }

        var grouped = new Dictionary<long, UsageAccumulator>();
        if (usageDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in usageDoc.RootElement.EnumerateArray())
            {
                var channelId = GetString(row, "channelIdentifier");
                var start = GetDateTimeOffset(row, "startTime");
                if (channelId is null || start is null || !row.TryGetProperty("kwh", out var kwhElement) || !kwhElement.TryGetDouble(out var kwh))
                    continue;
                if (!channelTypes.TryGetValue(channelId, out var channelType))
                    continue;

                var key = start.Value.UtcTicks;
                if (!grouped.TryGetValue(key, out var acc))
                    grouped[key] = acc = new UsageAccumulator(start.Value);
                var quality = GetString(row, "quality") ?? "unknown";
                acc.Billable &= quality.Equals("billable", StringComparison.OrdinalIgnoreCase);
                acc.Estimated |= quality.Equals("estimated", StringComparison.OrdinalIgnoreCase);

                if (channelType.Equals("general", StringComparison.OrdinalIgnoreCase))
                    acc.ImportKwh += Math.Max(0, kwh);
                else if (channelType.Equals("feedIn", StringComparison.OrdinalIgnoreCase))
                    acc.ExportKwh += Math.Abs(kwh);
            }
        }

        var intervals = new List<EnergyInterval>();
        var billableCount = 0;
        var estimatedCount = 0;
        foreach (var item in grouped.Values)
        {
            var importPrice = priceMap.TryGetValue((item.Start.UtcTicks, "general"), out var ip) ? ip.Price : (decimal?)null;
            var exportPrice = priceMap.TryGetValue((item.Start.UtcTicks, "feedIn"), out var ep) ? ep.Price : (decimal?)null;
            intervals.Add(new EnergyInterval(item.Start, item.ImportKwh, item.ExportKwh, importPrice, exportPrice, item.Billable));
            if (item.Billable) billableCount++;
            if (item.Estimated) estimatedCount++;
        }

        var summary = EnergyAccounting.Summarise(intervals);
        return new AmberDashboardSnapshot(
            siteId, network, intervalMinutes, currentImport, currentExport, summary,
            intervals.Count, summary.PricedImportIntervals, summary.PricedExportIntervals, billableCount, estimatedCount);
    }

    private static bool StringEquals(JsonElement element, string property, string value) =>
        GetString(element, property)?.Equals(value, StringComparison.OrdinalIgnoreCase) == true;

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string property) =>
        DateTimeOffset.TryParse(GetString(element, property), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime() : null;

    private sealed class UsageAccumulator(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; } = start;
        public double ImportKwh { get; set; }
        public double ExportKwh { get; set; }
        public bool Billable { get; set; } = true;
        public bool Estimated { get; set; }
    }
}
