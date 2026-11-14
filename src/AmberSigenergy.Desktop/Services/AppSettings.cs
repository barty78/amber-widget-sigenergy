using System.Text.Json;

namespace AmberSigenergy.Desktop.Services;

public sealed record AppSettings(
    double BatteryCapacityKwh,
    string? AmberSiteId,
    string SigenergyHost,
    int SigenergyPort,
    byte SigenergyUnitId,
    int PollIntervalSeconds)
{
    public static AppSettings Load()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.example.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.example.json"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "appsettings.example.json"))
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
            throw new FileNotFoundException("Could not find appsettings.json or appsettings.example.json. Keep the example file in the repository root, or create appsettings.json beside it.");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var battery = root.TryGetProperty("Battery", out var b) ? b : default;
        var amber = root.TryGetProperty("Amber", out var a) ? a : default;
        var sig = root.TryGetProperty("Sigenergy", out var s) ? s : default;

        return new AppSettings(
            battery.ValueKind == JsonValueKind.Object && battery.TryGetProperty("CapacityKwh", out var cap) ? cap.GetDouble() : 40d,
            amber.ValueKind == JsonValueKind.Object && amber.TryGetProperty("SiteId", out var site) && !string.IsNullOrWhiteSpace(site.GetString()) ? site.GetString() : null,
            sig.ValueKind == JsonValueKind.Object && sig.TryGetProperty("Host", out var host) ? host.GetString() ?? "127.0.0.1" : "127.0.0.1",
            sig.ValueKind == JsonValueKind.Object && sig.TryGetProperty("Port", out var port) ? port.GetInt32() : 502,
            sig.ValueKind == JsonValueKind.Object && sig.TryGetProperty("PlantUnitId", out var unit) ? checked((byte)unit.GetInt32()) : (byte)247,
            sig.ValueKind == JsonValueKind.Object && sig.TryGetProperty("PollIntervalSeconds", out var poll) ? Math.Clamp(poll.GetInt32(), 2, 60) : 5);
    }
}
