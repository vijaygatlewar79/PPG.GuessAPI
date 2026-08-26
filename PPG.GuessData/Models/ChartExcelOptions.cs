namespace PPG.GuessData.Models;

public sealed class ChartExcelOptions
{
    public required IReadOnlyList<ChartSourceOption> Sources { get; init; }
}
