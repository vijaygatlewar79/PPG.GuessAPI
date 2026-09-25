namespace PPG.GuessData.Models;

public sealed class PatternWiseAnalysisRequest
{
    public string FileName { get; init; } = string.Empty;

    public PanelNumberType NumberType { get; init; } = PanelNumberType.Open;

    public int LatestCount { get; init; } = 3;

    public int SkipLastNumbers { get; init; }

    public int TopCount { get; init; } = 10;

    public int DayCount { get; init; } = 7;

    // Uses the selected day-specific OPEN or CLOSE column as a three-digit panel value.
    public bool UseTripleNumbers { get; init; }

    public IReadOnlyList<PanelPatternType> Patterns { get; init; } = [];
}
