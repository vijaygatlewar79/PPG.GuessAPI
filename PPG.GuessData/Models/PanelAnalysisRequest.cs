namespace PPG.GuessData.Models;

public sealed class PanelAnalysisRequest
{
    public PanelPatternType Pattern { get; init; } = PanelPatternType.Sequence;

    public string FileName { get; init; } = string.Empty;

    public PanelNumberType NumberType { get; init; } = PanelNumberType.Open;

    public string Numbers { get; init; } = string.Empty;

    public int SkipLastNumbers { get; init; }
}
