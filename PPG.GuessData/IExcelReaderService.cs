using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IExcelReaderService
{
    Task<string?> ReadSourceUrlAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default);

    Task<PanelWorkbook> ReadPanelsAsync(
        Stream workbookStream,
        CancellationToken cancellationToken = default);
}
