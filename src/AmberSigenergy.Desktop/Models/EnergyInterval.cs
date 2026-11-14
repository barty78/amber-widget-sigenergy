namespace AmberSigenergy.Desktop.Models;

/// <summary>
/// One Amber meter interval. Prices are cents/kWh and energy is kWh.
/// </summary>
public sealed record EnergyInterval(
    DateTimeOffset Start,
    double ImportKwh,
    double ExportKwh,
    decimal? ImportPriceCentsPerKwh,
    decimal? ExportPriceCentsPerKwh,
    bool IsActual);

public sealed record EnergyAccountSummary(
    double ImportedKwh,
    double ExportedKwh,
    decimal ImportCost,
    decimal ExportEarnings,
    decimal NetEnergyValue,
    int PricedImportIntervals,
    int PricedExportIntervals);
