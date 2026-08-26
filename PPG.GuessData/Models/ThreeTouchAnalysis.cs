namespace PPG.GuessData.Models;

public sealed class ThreeTouchAnalysis
{
    public string AnchorSequence { get; init; } = string.Empty;

    public string AnchorDay { get; init; } = string.Empty;

    public required IReadOnlyList<ThreeTouchPoint> Touches { get; init; }

    public string RuleName { get; init; } = string.Empty;

    public string RuleDescription { get; init; } = string.Empty;

    public string PredictedNumber { get; init; } = string.Empty;

    public int BacktestWins { get; init; }

    public int BacktestAttempts { get; init; }

    public string Prompt { get; init; } = string.Empty;
}

public sealed class ThreeTouchPoint
{
    public string Label { get; init; } = string.Empty;

    public int AnchorRowId { get; init; }

    public string WeekDate { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;
}
