using System.Net.Http.Headers;
using System.Text.Json;

namespace AmberSigenergy.Desktop.Services;

public sealed class AmberApiClient
{
    private readonly HttpClient _http;

    public AmberApiClient(string bearerToken, HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
        _http.BaseAddress = new Uri("https://api.amber.com.au/v1/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    public async Task<JsonDocument> GetSitesAsync(CancellationToken ct = default) =>
        await GetJsonAsync("sites", ct);

    public async Task<JsonDocument> GetCurrentPricesAsync(string siteId, int next = 6, int previous = 1, CancellationToken ct = default)
    {
        var path = $"sites/{Uri.EscapeDataString(siteId)}/prices/current?next={next}&previous={previous}&resolution=30";
        return await GetJsonAsync(path, ct);
    }

    public async Task<JsonDocument> GetUsageAsync(string siteId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        if (end < start) throw new ArgumentException("End date must be on or after start date.");
        if (end.DayNumber - start.DayNumber > 89) throw new ArgumentOutOfRangeException(nameof(end), "Amber usage API supports up to 90 days per request.");
        var path = $"sites/{Uri.EscapeDataString(siteId)}/usage?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}&resolution=30";
        return await GetJsonAsync(path, ct);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Amber rejected the API token. Check it in the app settings.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }
}
