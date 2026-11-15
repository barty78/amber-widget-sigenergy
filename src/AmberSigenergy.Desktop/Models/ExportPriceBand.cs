namespace AmberSigenergy.Desktop.Models;

public sealed record ExportPriceBand(
    DateTimeOffset BlockStart,
    decimal LowCents,
    decimal MidCents,
    decimal HighCents,
    string Source);
