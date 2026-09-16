using System.ComponentModel.DataAnnotations;

namespace PPG.GuessAPI.Models;

public sealed class GenericPredictionRequest
{
    [Required]
    [StringLength(100_000, MinimumLength = 1)]
    public string SeriesData { get; init; } = string.Empty;

    [Range(1, 7)]
    public int Period { get; init; } = 7;

    [Range(20, 500)]
    public int BacktestSize { get; init; } = 120;
}
