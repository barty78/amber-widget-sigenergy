namespace AmberSigenergy.Desktop.Models;

public sealed record SigenergyDailyTotals(
    DateOnly Date,
    double ImportedKwh,
    double ExportedKwh,
    double SolarGeneratedKwh,
    double HomeLoadKwh,
    double BatteryChargedKwh,
    double BatteryDischargedKwh,
    int Samples);
