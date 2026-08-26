namespace PPG.GuessData.Models;

public sealed class PanelGame
{
    public required string FileName { get; init; }

    public required string DisplayName { get; init; }

    public string? SourceUrl { get; init; }
}
