namespace PPG.GuessData.Models;

public sealed class ChartSourceOption
{
    public required string FileName { get; init; }

    public required string DisplayName { get; init; }

    public int OrderBy { get; init; } = int.MaxValue;

    public required string Url { get; init; }
}
