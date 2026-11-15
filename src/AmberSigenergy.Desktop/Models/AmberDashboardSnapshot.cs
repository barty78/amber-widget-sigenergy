namespace AmberSigenergy.Desktop.Models;

public sealed record AmberDashboardSnapshot(
    string SiteId,
    string Network,
    int IntervalMinutes,
    decimal? CurrentImportPriceCents,
    decimal? CurrentExportPriceCents,
    EnergyAccountSummary Today,
    int UsageIntervals,
    int PricedImportIntervals,
    int PricedExportIntervals,
    int BillableIntervals,
    int EstimatedIntervals,
    IReadOnlyList<ExportPriceBand> ExportPriceBands);
