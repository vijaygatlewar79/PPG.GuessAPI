using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IExcelReaderService
{
    Task<string?> ReadSourceUrlAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<PanelWorkbook> ReadPanelsAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
