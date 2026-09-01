using System.Text.Json;
using PPG.GuessData;
using PPG.GuessData.Models;

namespace PPG.GuessAPI;

public sealed class ChartSourceCatalog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IPanelFileStorage _fileStorage;
    private readonly string _seedCatalogPath;
    private readonly SemaphoreSlim _catalogLock = new(1, 1);

    public ChartSourceCatalog(
        IPanelFileStorage fileStorage,
        IWebHostEnvironment environment)
    {
        _fileStorage = fileStorage;
        _seedCatalogPath = Path.Combine(environment.ContentRootPath, "chart-sources.json");
    }

    public async Task<ChartExcelOptions> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            return new ChartExcelOptions { Sources = await ReadSourcesAsync(cancellationToken) };
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task SaveAsync(
        string fileName,
        string? displayName,
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            var sources = (await ReadSourcesAsync(cancellationToken)).ToList();
            var requestedFileName = fileName.Trim();
            var existingIndex = sources.FindIndex(source => string.Equals(
                source.FileName,
                requestedFileName,
                StringComparison.OrdinalIgnoreCase));
            var updatedSource = new ChartSourceOption
            {
                FileName = fileName.Trim(),
                DisplayName = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName.Trim()
                    : existingIndex >= 0
                    ? sources[existingIndex].DisplayName
                    : Path.GetFileNameWithoutExtension(fileName.Trim()),
                OrderBy = existingIndex >= 0
                    ? sources[existingIndex].OrderBy
                    : sources
                        .Where(source => source.OrderBy < int.MaxValue)
                        .Select(source => source.OrderBy)
                        .DefaultIfEmpty(0)
                        .Max() + 1,
                Url = url.Trim()
            };

            if (existingIndex >= 0)
            {
                sources[existingIndex] = updatedSource;
            }
            else
            {
                sources.Add(updatedSource);
            }

            await WriteSourcesAsync(sources, cancellationToken);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<string?> GetFileNameAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            var requestedFileName = fileName.Trim();
            var source = (await ReadSourcesAsync(cancellationToken)).FirstOrDefault(candidate =>
                string.Equals(candidate.FileName, requestedFileName, StringComparison.OrdinalIgnoreCase));

            return source is null
                ? null
                : await ResolveStoredFileNameAsync(
                    ValidateConfiguredFileName(source.FileName),
                    cancellationToken);
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string fileName,
        ExcelFileBackupAction backupAction = ExcelFileBackupAction.Remove,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            var sources = (await ReadSourcesAsync(cancellationToken)).ToList();
            var requestedFileName = fileName.Trim();
            var existingIndex = sources.FindIndex(source => string.Equals(
                source.FileName,
                requestedFileName,
                StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                return false;
            }

            var source = sources[existingIndex];
            var configuredFileName = await ResolveStoredFileNameAsync(
                ValidateConfiguredFileName(source.FileName),
                cancellationToken);

            sources.RemoveAt(existingIndex);
            try
            {
                await WriteSourcesAsync(sources, cancellationToken);
                await _fileStorage.DeleteExcelFileAsync(
                    configuredFileName,
                    backupAction,
                    cancellationToken);
            }
            catch
            {
                try
                {
                    await WriteSourcesAsync([.. sources, source], CancellationToken.None);
                }
                catch
                {
                    // Preserve the original storage exception. The workbook backup,
                    // when created, remains available for manual recovery.
                }
                throw;
            }

            return true;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    private static string ValidateConfiguredFileName(string fileName)
    {
        if (fileName.IndexOfAny(['/', '\\']) >= 0
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The configured file name is invalid.", nameof(fileName));
        }

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only configured .xlsx or .xlsm files are supported.", nameof(fileName));
        }

        return fileName;
    }

    private async Task<IReadOnlyList<ChartSourceOption>> ReadSourcesAsync(CancellationToken cancellationToken)
    {
        var json = await _fileStorage.ReadCatalogAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json) && File.Exists(_seedCatalogPath))
        {
            json = await File.ReadAllTextAsync(_seedCatalogPath, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var sources = JsonSerializer.Deserialize<List<ChartSourceOption>>(
            json,
            SerializerOptions) ?? [];

        return sources
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.FileName) &&
                !string.IsNullOrWhiteSpace(source.DisplayName) &&
                !string.IsNullOrWhiteSpace(source.Url))
            .OrderBy(source => source.OrderBy)
            .ThenBy(source => source.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task WriteSourcesAsync(
        IReadOnlyList<ChartSourceOption> sources,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sources, SerializerOptions);
        await _fileStorage.WriteCatalogAsync(json, cancellationToken);
    }

    private async Task<string> ResolveStoredFileNameAsync(
        string configuredFileName,
        CancellationToken cancellationToken)
    {
        var fileNames = await _fileStorage.ListExcelFileNamesAsync(cancellationToken);
        return fileNames.FirstOrDefault(fileName => string.Equals(
                   fileName,
                   configuredFileName,
                   StringComparison.Ordinal))
               ?? fileNames.FirstOrDefault(fileName => string.Equals(
                   fileName,
                   configuredFileName,
                   StringComparison.OrdinalIgnoreCase))
               ?? configuredFileName;
    }

}
