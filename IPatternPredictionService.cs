namespace PPG.GuessAPI;

public interface IPatternPredictionService
{
    bool IsConfigured { get; }

    string Model { get; }

    Task<string> PredictAsync(
        string seriesData,
        PatternPredictionMode mode = PatternPredictionMode.Standard,
        CancellationToken cancellationToken = default);
}
