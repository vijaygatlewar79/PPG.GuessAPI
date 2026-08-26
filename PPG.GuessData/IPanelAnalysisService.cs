using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IPanelAnalysisService
{
    PanelAnalysisResult Analyze(
        IReadOnlyList<Panel> panels,
        IReadOnlyList<string> availableDays,
        string numbers,
        PanelNumberType numberType = PanelNumberType.Open,
        PanelPatternType pattern = PanelPatternType.Sequence,
        int skipLastNumbers = 0);
}
