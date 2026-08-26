namespace PPG.GuessData.Models;

public sealed class MatchLine
{
    public int CurrentDataRowId { get; init; }

    public string WeekDate { get; init; } = string.Empty;

    public string NextNumber { get; init; } = string.Empty;
}
