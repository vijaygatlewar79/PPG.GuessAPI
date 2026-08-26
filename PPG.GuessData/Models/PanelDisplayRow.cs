namespace PPG.GuessData.Models;

public sealed class PanelDisplayRow
{
    public int Id { get; init; }

    public string WeekDate { get; init; } = string.Empty;

    public required IReadOnlyDictionary<string, PanelDayValue> Days { get; init; }
}
