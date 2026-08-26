using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IChartExcelService
{
    Task<ChartExcelResult> GenerateExcelAsync(
        ChartExcelRequest request,
        string filesDirectory,
        string backupDirectory,
        CancellationToken cancellationToken = default);
}
