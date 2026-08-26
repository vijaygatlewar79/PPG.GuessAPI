using System.ComponentModel.DataAnnotations;

namespace PPG.GuessAPI.Models;

public sealed class PatternPredictionRequest
{
    [Required]
    [StringLength(50_000, MinimumLength = 1)]
    public string SeriesData { get; init; } = string.Empty;

    public string PredictionMode { get; init; } = "Standard";
}
