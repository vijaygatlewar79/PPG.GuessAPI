namespace PPG.GuessData.Models;

public sealed class CurrentDataRow
{
    public int Id { get; init; }

    public string DayOfWeek { get; init; } = string.Empty;

    public string Number { get; init; } = string.Empty;

    public string WeekDate { get; init; } = string.Empty;
}
