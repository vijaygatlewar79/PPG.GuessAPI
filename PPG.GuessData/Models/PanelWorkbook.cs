namespace PPG.GuessData.Models;

public sealed class PanelWorkbook
{
    public required IReadOnlyList<string> AvailableDays { get; init; }

    public required IReadOnlyList<Panel> Panels { get; init; }
}
