using System.IO;
using System.Text.Json;
using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop.Services;

/// <summary>
/// Uses native Sigenergy daily counters where available. Grid import/export are integrated
/// from the grid CT power because V2.5 does not define reliable daily grid energy counters.
/// </summary>
public sealed class SigenergyDailyEnergyTracker
{
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AmberSigenergy", "sigenergy-daily.json");
    private readonly TimeSpan _maximumGap = TimeSpan.FromMinutes(2);
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private double _imported, _exported, _solar, _load, _charged, _discharged;
    private double? _nativeSolar, _nativeCharge, _nativeDischarge;
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
        catch { }
    }

    public SigenergyDailyTotals Totals
    {
        get
        {
            var solar = _nativeSolar ?? _solar;
            var charge = _nativeCharge ?? _charged;
            var discharge = _nativeDischarge ?? _discharged;
            // Home load is integrated directly from the live Modbus power balance while the app runs.\n            // The energy-balance fallback is retained for days with no usable samples.\n            var home = _samples > 1 && _load > 0 ? _load : Math.Max(0, solar - charge + discharge + _imported - _exported);
            return new SigenergyDailyTotals(_date, _imported, _exported, solar, home, charge, discharge, _samples);
        }
    }

    public bool HasNativeDailyCounters => _nativeCharge.HasValue && _nativeDischarge.HasValue;
    public bool HasNativePvCounter => _nativeSolar.HasValue;

    public double GetHomeLoadKw(SigenergyTelemetry t) =>
        Math.Max(0, Math.Max(0, t.PvPowerKw ?? 0) + t.GridPowerKw - (t.BatteryPowerKw ?? 0));

    public void AddSample(SigenergyTelemetry t)
    {
        var today = DateOnly.FromDateTime(t.ReadAt.LocalDateTime);
        if (today != _date)
        {
            _date = today;
            _imported = _exported = _solar = _load = _charged = _discharged = 0;
            _nativeSolar = _nativeCharge = _nativeDischarge = null;
            _samples = 0;
            _last = null;
            _lastAt = null;
        }

        if (t.DailySolarGeneratedKwh is >= 0 and < 1000) _nativeSolar = t.DailySolarGeneratedKwh;
        if (t.DailyBatteryChargedKwh is >= 0 and < 10000) _nativeCharge = t.DailyBatteryChargedKwh;
        if (t.DailyBatteryDischargedKwh is >= 0 and < 10000) _nativeDischarge = t.DailyBatteryDischargedKwh;

        var current = new SampleValues(
            Math.Max(0, t.GridPowerKw),
            Math.Max(0, -t.GridPowerKw),
            Math.Max(0, t.PvPowerKw ?? 0),
            GetHomeLoadKw(t),
            Math.Max(0, t.BatteryPowerKw ?? 0),
            Math.Max(0, -(t.BatteryPowerKw ?? 0)));

        if (_last is not null && _lastAt is not null)
        {
            var elapsed = t.ReadAt - _lastAt.Value;
            var hours = elapsed.TotalHours;
            if (hours > 0 && elapsed <= _maximumGap)
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
        if (DateTimeOffset.Now - _lastSave >= TimeSpan.FromMinutes(1)) Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Totals, new JsonSerializerOptions { WriteIndented = true }));
            _lastSave = DateTimeOffset.Now;
        }
        catch { }
    }

    private static double Average(double a, double b) => (a + b) / 2d;
    private sealed record SampleValues(double ImportKw, double ExportKw, double SolarKw, double LoadKw, double ChargeKw, double DischargeKw);
}
