namespace PPG.GuessData.Models;

public sealed class PanelAnalysisResult
{
    public PanelPatternType Pattern { get; init; }

    public PanelNumberType NumberType { get; init; }

    public string GuessNumbers { get; init; } = string.Empty;

    public required IReadOnlyList<string> LatestNumbers { get; init; }

    public required IReadOnlyList<string> AvailableDays { get; init; }

    public required IReadOnlyList<CurrentDataRow> CurrentData { get; init; }

    public required IReadOnlyList<CurrentDataWeek> CurrentDataWeeks { get; init; }

    public required IReadOnlyList<int> MatchingRowIds { get; init; }

    public required IReadOnlyList<MatchLine> MatchLines { get; init; }

    public required IReadOnlyList<NextNumberCount> NextNumberCounts { get; init; }

    public ThreeTouchAnalysis? ThreeTouch { get; init; }

    public required IReadOnlyList<Panel> Panels { get; init; }

    public required IReadOnlyList<PanelDisplayRow> PanelRows { get; init; }
}
