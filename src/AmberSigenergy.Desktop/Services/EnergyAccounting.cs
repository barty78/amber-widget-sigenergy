using AmberSigenergy.Desktop.Models;

namespace AmberSigenergy.Desktop.Services;

public static class EnergyAccounting
{
    /// <summary>
    /// Calculates gross interval-based energy value. Supply charges, subscription,
    /// demand charges, taxes and other bill adjustments are intentionally excluded.
    /// Missing prices are not silently treated as zero.
    /// </summary>
    public static EnergyAccountSummary Summarise(IEnumerable<EnergyInterval> intervals)
    {
        var rows = intervals.ToArray();
        var importCost = rows.Where(x => x.ImportPriceCentsPerKwh.HasValue)
            .Sum(x => (decimal)x.ImportKwh * x.ImportPriceCentsPerKwh!.Value / 100m);
        var exportEarnings = rows.Where(x => x.ExportPriceCentsPerKwh.HasValue)
            .Sum(x => (decimal)x.ExportKwh * x.ExportPriceCentsPerKwh!.Value / 100m);

        return new EnergyAccountSummary(
            rows.Sum(x => x.ImportKwh),
            rows.Sum(x => x.ExportKwh),
            decimal.Round(importCost, 4),
            decimal.Round(exportEarnings, 4),
            decimal.Round(exportEarnings - importCost, 4),
            rows.Count(x => x.ImportKwh > 0 && x.ImportPriceCentsPerKwh.HasValue),
            rows.Count(x => x.ExportKwh > 0 && x.ExportPriceCentsPerKwh.HasValue));
    }
}
