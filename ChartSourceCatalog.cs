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

    private readonly string _catalogPath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public ChartSourceCatalog(IWebHostEnvironment environment)
    {
        _catalogPath = Path.Combine(environment.ContentRootPath, "chart-sources.json");
    }

    public async Task<ChartExcelOptions> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            return new ChartExcelOptions { Sources = await ReadSourcesAsync(cancellationToken) };
        }
        finally
        {
            _fileLock.Release();
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

        await _fileLock.WaitAsync(cancellationToken);
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
            _fileLock.Release();
        }
    }

    public async Task<string?> GetFilePathAsync(
        string fileName,
        string filesDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var requestedFileName = fileName.Trim();
            var source = (await ReadSourcesAsync(cancellationToken)).FirstOrDefault(candidate =>
                string.Equals(candidate.FileName, requestedFileName, StringComparison.OrdinalIgnoreCase));

            return source is null
                ? null
                : ResolveConfiguredFilePath(source.FileName, filesDirectory);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        string fileName,
        string filesDirectory,
        string backupDirectory,
        ExcelFileBackupAction backupAction = ExcelFileBackupAction.Remove,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        await _fileLock.WaitAsync(cancellationToken);
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
            var directoryPath = Path.GetFullPath(filesDirectory);
            var filePath = ResolveConfiguredFilePath(source.FileName, filesDirectory);

            string? stagedPath = null;
            if (File.Exists(filePath))
            {
                ExcelFileBackup.Create(filePath, backupDirectory, backupAction);
                stagedPath = Path.Combine(directoryPath, $".delete-{Guid.NewGuid():N}.tmp");
                File.Move(filePath, stagedPath);
            }

            sources.RemoveAt(existingIndex);
            try
            {
                await WriteSourcesAsync(sources, cancellationToken);
                if (stagedPath is not null)
                {
                    File.Delete(stagedPath);
                }
            }
            catch
            {
                if (stagedPath is not null && File.Exists(stagedPath) && !File.Exists(filePath))
                {
                    File.Move(stagedPath, filePath);
                }
                await WriteSourcesAsync([.. sources, source], CancellationToken.None);
                throw;
            }

            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static string ResolveConfiguredFilePath(string fileName, string filesDirectory)
    {
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The configured file name is invalid.", nameof(fileName));
        }

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only configured .xlsx or .xlsm files are supported.", nameof(fileName));
        }

        var directoryPath = Path.GetFullPath(filesDirectory);
        var directoryPrefix = directoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(directoryPath, fileName));
        if (!filePath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The configured file must be inside the API Files folder.", nameof(fileName));
        }

        return filePath;
    }

    private async Task<IReadOnlyList<ChartSourceOption>> ReadSourcesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_catalogPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_catalogPath);
        var sources = await JsonSerializer.DeserializeAsync<List<ChartSourceOption>>(
            stream,
            SerializerOptions,
            cancellationToken) ?? [];

        return sources
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.FileName) &&
                !string.IsNullOrWhiteSpace(source.DisplayName) &&
                !string.IsNullOrWhiteSpace(source.Url))
            .OrderBy(source => source.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task WriteSourcesAsync(
        IReadOnlyList<ChartSourceOption> sources,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{_catalogPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, sources, SerializerOptions, cancellationToken);
            }

            File.Move(temporaryPath, _catalogPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

}
