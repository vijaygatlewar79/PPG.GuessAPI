namespace PPG.GuessAPI.Models;

public sealed class GenericPredictionResult
{
    public required string Prompt { get; init; }

    public required IReadOnlyList<GenericNumberPrompt> NumberPrompts { get; init; }

    public required string Prediction { get; init; }

    public required string PredictedNumber { get; init; }

    public required IReadOnlyList<string> PredictedNumbers { get; init; }

    public required string Model { get; init; }

    public required IReadOnlyList<GenericDigitScore> Scores { get; init; }

    public int BacktestAttempts { get; init; }

    public int BacktestHits { get; init; }

    public double BacktestHitRate { get; init; }
}

public sealed class GenericNumberPrompt
{
    public int Position { get; init; }

    public required string ActualNumber { get; init; }

    public required IReadOnlyList<string> PredictedNumbers { get; init; }

    public bool IsPass { get; init; }

    public required string Prompt { get; init; }
}

public sealed class GenericDigitScore
{
    public required string Number { get; init; }

    public double Score { get; init; }

    public int Rank { get; init; }
}
