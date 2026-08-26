namespace PPG.GuessData;

public interface IPanelFileStorage
{
    Task<IReadOnlyList<string>> ListExcelFileNamesAsync(
        CancellationToken cancellationToken = default);

    Task<Stream> OpenExcelFileAsync(
        string fileName,
        CancellationToken cancellationToken = default);

    Task<bool> ExcelFileExistsAsync(
        string fileName,
        CancellationToken cancellationToken = default);

    Task SaveExcelFileAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        ExcelFileBackupAction backupAction,
        CancellationToken cancellationToken = default);

    Task DeleteExcelFileAsync(
        string fileName,
        ExcelFileBackupAction backupAction,
        CancellationToken cancellationToken = default);

    Task<string?> ReadCatalogAsync(
        CancellationToken cancellationToken = default);

    Task WriteCatalogAsync(
        string catalogJson,
        CancellationToken cancellationToken = default);
}
