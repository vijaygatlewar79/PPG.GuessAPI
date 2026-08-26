namespace PPG.GuessData.Models;

public sealed class PanelDayValue
{
    public string Open { get; init; } = string.Empty;

    public string Pair { get; init; } = string.Empty;

    public string Close { get; init; } = string.Empty;

    public bool IsRedPair { get; init; }
}
