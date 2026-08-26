using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IChartExcelService
{
    Task<ChartExcelResult> GenerateExcelAsync(
        ChartExcelRequest request,
        CancellationToken cancellationToken = default);
}
