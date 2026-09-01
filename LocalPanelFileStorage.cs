using System.Globalization;
using Microsoft.Extensions.Hosting;
using PPG.GuessData;

namespace PPG.GuessAPI;

public sealed class LocalPanelFileStorage : IPanelFileStorage
{
    private static readonly TimeSpan IndiaStandardTimeOffset = TimeSpan.FromMinutes(330);
    private readonly string _rootFolder;
    private readonly string _backupRoot;
    private readonly string _catalogPath;

        public LocalPanelFileStorage(IHostEnvironment env)
        {
            ArgumentNullException.ThrowIfNull(env);

            // Try to locate the project's Files folder. When running from the build output
            // the ContentRootPath may be the output folder (bin/Debug/netX). Walk up
            // a few directory levels to find a Files folder in the repo if present.
            var candidates = new[]
            {
                Path.Combine(env.ContentRootPath, "Files"),
                Path.Combine(env.ContentRootPath, "..", "Files"),
                Path.Combine(env.ContentRootPath, "..", "..", "Files"),
                Path.Combine(env.ContentRootPath, "..", "..", "..", "Files"),
            };

            var found = candidates.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
            if (found is not null)
            {
                _rootFolder = found;
            }
            else
            {
                _rootFolder = Path.Combine(env.ContentRootPath, "Files");
                Directory.CreateDirectory(_rootFolder);
            }

            // Use a backup folder alongside the chosen root Files folder.
            _backupRoot = Path.Combine(Path.GetDirectoryName(_rootFolder) ?? env.ContentRootPath, "FilesBackup");
            Directory.CreateDirectory(_backupRoot);

            // chart-sources.json is a project-level configuration file. Keeping it
            // beside the app settings also makes edits visible immediately in the UI.
            _catalogPath = Path.Combine(env.ContentRootPath, "chart-sources.json");
        }

    public Task<IReadOnlyList<string>> ListExcelFileNamesAsync(CancellationToken cancellationToken = default)
    {
            var files = Directory.EnumerateFiles(_rootFolder)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n) && IsRootExcelFileName(n!))
                .OrderBy(n => n!, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        return Task.FromResult((IReadOnlyList<string>)files);
    }

    public Task<Stream> OpenExcelFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var validated = ValidateRootExcelFileName(fileName);
        var path = Path.Combine(_rootFolder, validated);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The Excel file '{validated}' was not found in local storage.", validated);
        }

        var ms = new MemoryStream(File.ReadAllBytes(path), writable: false);
        return Task.FromResult<Stream>(ms);
    }

    public Task<bool> ExcelFileExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var validated = ValidateRootExcelFileName(fileName);
        var path = Path.Combine(_rootFolder, validated);
        return Task.FromResult(File.Exists(path));
    }

    public Task SaveExcelFileAsync(string fileName, ReadOnlyMemory<byte> content, ExcelFileBackupAction backupAction, CancellationToken cancellationToken = default)
    {
        var validated = ValidateRootExcelFileName(fileName);
        var path = Path.Combine(_rootFolder, validated);

        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            Backup(validated, existing, backupAction);
        }

        File.WriteAllBytes(path, content.ToArray());
        return Task.CompletedTask;
    }

    public Task DeleteExcelFileAsync(string fileName, ExcelFileBackupAction backupAction, CancellationToken cancellationToken = default)
    {
        var validated = ValidateRootExcelFileName(fileName);
        var path = Path.Combine(_rootFolder, validated);
        if (!File.Exists(path)) return Task.CompletedTask;

        var existing = File.ReadAllBytes(path);
        Backup(validated, existing, backupAction);
        File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<string?> ReadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_catalogPath)) return Task.FromResult<string?>(null);
        var json = File.ReadAllText(_catalogPath);
        return Task.FromResult<string?>(json);
    }

    public Task WriteCatalogAsync(string catalogJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogJson);
        File.WriteAllText(_catalogPath, catalogJson);
        return Task.CompletedTask;
    }

    private void Backup(string fileName, byte[] content, ExcelFileBackupAction action)
    {
        var timestamp = DateTimeOffset.UtcNow.ToOffset(IndiaStandardTimeOffset);
        var backupFileName = string.Concat(
            Path.GetFileNameWithoutExtension(fileName),
            "_",
            timestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture),
            "_",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            "_IST",
            Path.GetExtension(fileName));

        var backupDir = Path.Combine(_backupRoot, timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), action.ToString());
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, backupFileName);
        File.WriteAllBytes(backupPath, content);
    }

    private static string ValidateRootExcelFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(fileName, fileName.Trim(), StringComparison.Ordinal)
            || !IsRootExcelFileName(fileName))
        {
            throw new ArgumentException(
                "The file name must be a non-temporary root .xlsx or .xlsm file name.",
                nameof(fileName));
        }

        return fileName;
    }

    private static bool IsRootExcelFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(new[] {'/', '\\'}) >= 0
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.StartsWith(".", StringComparison.Ordinal)
            || fileName.StartsWith("~$", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }
}
