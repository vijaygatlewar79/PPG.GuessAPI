namespace PPG.GuessData.Models;

public sealed class CurrentDataWeek
{
    public int Id { get; init; }

    public string WeekDate { get; init; } = string.Empty;

    public required IReadOnlyDictionary<string, CurrentDataRow> Days { get; init; }
}
