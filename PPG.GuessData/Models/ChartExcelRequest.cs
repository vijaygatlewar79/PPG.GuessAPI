namespace PPG.GuessData.Models;

public sealed class ChartExcelRequest
{
    public string Url { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}
