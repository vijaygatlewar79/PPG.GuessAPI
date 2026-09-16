namespace PPG.GuessData.Models;

public sealed class PatternWiseAnalysisRow
{
    public required PanelPatternType Pattern { get; init; }

    public required IReadOnlyList<PatternWiseDayResult> Results { get; init; }

    public int PassedCount { get; init; }

    public int EvaluatedCount { get; init; }

    public string TodayGuessDay { get; init; } = string.Empty;

    public required IReadOnlyList<string> TodayNumbers { get; init; }
}

public sealed class PatternWiseDayResult
{
    public string DayGuess { get; init; } = string.Empty;

    public required IReadOnlyList<string> Numbers { get; init; }

    public string PassNumber { get; init; } = string.Empty;

    // One-based position of the actual number in the ranked guesses; null means no match.
    public int? MatchRank { get; init; }
}
