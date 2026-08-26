using System.Globalization;

namespace PPG.GuessData;

public enum ExcelFileBackupAction
{
    Update,
    Remove
}

public static class ExcelFileBackup
{
    private static readonly TimeSpan IndiaStandardTimeOffset = TimeSpan.FromMinutes(330);

    public static string Create(
        string sourceFilePath,
        string backupRootDirectory,
        ExcelFileBackupAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRootDirectory);

        var sourcePath = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The Excel file to back up was not found.", sourcePath);
        }

        var timestamp = DateTimeOffset.UtcNow.ToOffset(IndiaStandardTimeOffset);
        var dateDirectory = Path.Combine(
            Path.GetFullPath(backupRootDirectory),
            timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            action.ToString());
        Directory.CreateDirectory(dateDirectory);

        var fileName = Path.GetFileName(sourcePath);
        var backupName = string.Concat(
            Path.GetFileNameWithoutExtension(fileName),
            "_",
            timestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture),
            "_IST",
            Path.GetExtension(fileName));
        var backupPath = GetAvailableBackupPath(dateDirectory, backupName);

        File.Copy(sourcePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static string GetAvailableBackupPath(string directory, string fileName)
    {
        var candidatePath = Path.Combine(directory, fileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var copyNumber = 2; ; copyNumber++)
        {
            candidatePath = Path.Combine(directory, $"{name}_{copyNumber}{extension}");
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }
    }
}
