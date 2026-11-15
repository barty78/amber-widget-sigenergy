using System.IO;
using System.Text.Json;
using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop.Services;

/// <summary>
/// Estimates daily energy by integrating read-only live Modbus power samples.
/// These are monitoring estimates, not meter-settled or billing values.
/// </summary>
public sealed class SigenergyDailyEnergyTracker
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmberSigenergy", "sigenergy-daily.json");
    private readonly TimeSpan _maximumGap = TimeSpan.FromMinutes(2);
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private double _imported, _exported, _solar, _load, _charged, _discharged;
    private int _samples;
    private DateTimeOffset? _lastAt;
    private SampleValues? _last;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public SigenergyDailyEnergyTracker()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var saved = JsonSerializer.Deserialize<SigenergyDailyTotals>(File.ReadAllText(_filePath));
            if (saved is null || saved.Date != _date) return;
            _imported = saved.ImportedKwh;
            _exported = saved.ExportedKwh;
            _solar = saved.SolarGeneratedKwh;
            _load = saved.HomeLoadKwh;
            _charged = saved.BatteryChargedKwh;
            _discharged = saved.BatteryDischargedKwh;
            _samples = saved.Samples;
        }
        catch
        {
            // A corrupt local history file must not prevent live monitoring.
        }
    }

    public SigenergyDailyTotals Totals => new(_date, _imported, _exported, _solar, _load, _charged, _discharged, _samples);

    public double GetHomeLoadKw(SigenergyTelemetry t) =>
        Math.Max(0, Math.Max(0, t.PvPowerKw ?? 0) + t.GridPowerKw - (t.BatteryPowerKw ?? 0));

    public void AddSample(SigenergyTelemetry t)
    {
        var today = DateOnly.FromDateTime(t.ReadAt.LocalDateTime);
        if (today != _date)
        {
            _date = today;
            _imported = _exported = _solar = _load = _charged = _discharged = 0;
            _samples = 0;
            _last = null;
            _lastAt = null;
        }

        var current = new SampleValues(
            Math.Max(0, t.GridPowerKw),
            Math.Max(0, -t.GridPowerKw),
            Math.Max(0, t.PvPowerKw ?? 0),
            GetHomeLoadKw(t),
            Math.Max(0, t.BatteryPowerKw ?? 0),
            Math.Max(0, -(t.BatteryPowerKw ?? 0)));

        if (_last is not null && _lastAt is not null)
        {
            var hours = (t.ReadAt - _lastAt.Value).TotalHours;
            if (hours > 0 && t.ReadAt - _lastAt.Value <= _maximumGap)
            {
                _imported += Average(_last.ImportKw, current.ImportKw) * hours;
                _exported += Average(_last.ExportKw, current.ExportKw) * hours;
                _solar += Average(_last.SolarKw, current.SolarKw) * hours;
                _load += Average(_last.LoadKw, current.LoadKw) * hours;
                _charged += Average(_last.ChargeKw, current.ChargeKw) * hours;
                _discharged += Average(_last.DischargeKw, current.DischargeKw) * hours;
            }
        }

        _last = current;
        _lastAt = t.ReadAt;
        _samples++;

        if (DateTimeOffset.Now - _lastSave >= TimeSpan.FromMinutes(1))
            Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Totals, new JsonSerializerOptions { WriteIndented = true }));
            _lastSave = DateTimeOffset.Now;
        }
        catch
        {
            // Monitoring continues if local persistence is unavailable.
        }
    }

    private static double Average(double a, double b) => (a + b) / 2d;

    private sealed record SampleValues(double ImportKw, double ExportKw, double SolarKw, double LoadKw, double ChargeKw, double DischargeKw);
}
