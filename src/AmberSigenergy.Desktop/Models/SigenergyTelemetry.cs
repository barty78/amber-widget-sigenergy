namespace AmberSigenergy.Desktop.Models;

public sealed record SigenergyTelemetry(
    DateTimeOffset ReadAt,
    double GridPowerKw,
    double? BatterySocPercent,
    double? PlantPowerKw,
    double? PvPowerKw,
    double? BatteryPowerKw,
    double? RatedBatteryCapacityKwh,
    double? DailySolarGeneratedKwh = null,
    double? DailyBatteryChargedKwh = null,
    double? DailyBatteryDischargedKwh = null);
