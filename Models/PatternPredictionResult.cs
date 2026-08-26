namespace PPG.GuessAPI.Models;

public sealed class PatternPredictionResult
{
    public required string Prediction { get; init; }

    public required string PredictedNumber { get; init; }

    public required IReadOnlyList<string> PredictedNumbers { get; init; }

    public required string Model { get; init; }
}
