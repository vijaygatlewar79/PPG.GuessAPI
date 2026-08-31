namespace PPG.GuessData.Models;

public sealed class LastWeekAnalysisRequest
{
    public string FileName { get; init; } = string.Empty;

    public PanelNumberType NumberType { get; init; } = PanelNumberType.Open;

    public int LatestCount { get; init; } = 3;

    public int SkipLastNumbers { get; init; }

    public IReadOnlyList<PanelPatternType> Patterns { get; init; } = [];
}
