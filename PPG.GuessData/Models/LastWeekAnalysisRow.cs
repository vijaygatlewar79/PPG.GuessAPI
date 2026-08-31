namespace PPG.GuessData.Models;

public sealed class LastWeekAnalysisRow
{
    public string DayGuess { get; init; } = string.Empty;

    public required IReadOnlyList<string> Numbers { get; init; }

    public string PassNumber { get; init; } = string.Empty;
}
