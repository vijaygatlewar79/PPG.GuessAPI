using PPG.GuessData.Models;

namespace PPG.GuessData;

public sealed class PanelGameService : IPanelGameService
{
    private readonly IExcelReaderService _excelReaderService;

    public PanelGameService(IExcelReaderService excelReaderService)
    {
        _excelReaderService = excelReaderService;
    }

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xlsm" };

    public async Task<IReadOnlyList<PanelGame>> GetAvailableGamesAsync(
        string filesDirectory,
        CancellationToken cancellationToken = default)
    {
        var games = new List<PanelGame>();
        foreach (var path in GetGameFilePaths(filesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? sourceUrl;
            try
            {
                sourceUrl = await _excelReaderService.ReadSourceUrlAsync(path, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Source metadata is optional; a bad Source sheet must not hide the game.
                sourceUrl = null;
            }

            var fileName = Path.GetFileName(path);
            games.Add(new PanelGame
            {
                FileName = fileName,
                DisplayName = BuildDisplayName(fileName),
                SourceUrl = sourceUrl
            });
        }

        return games
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(game => game.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string ResolveGameFilePath(string filesDirectory, string? fileName)
    {
        var gamePaths = GetGameFilePaths(filesDirectory);
        if (gamePaths.Count == 0)
        {
            throw new FileNotFoundException("No supported Excel game files were found.", filesDirectory);
        }

        var gamePath = string.IsNullOrWhiteSpace(fileName)
            ? gamePaths
                .OrderBy(path => BuildDisplayName(Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .First()
            : gamePaths.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

        if (gamePath is null)
        {
            throw new ArgumentException("Select a valid game file.", nameof(fileName));
        }

        return gamePath;
    }

    private static IReadOnlyList<string> GetGameFilePaths(string filesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filesDirectory);

        if (!Directory.Exists(filesDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(filesDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .ToArray();
    }

    private static string BuildDisplayName(string fileName) => string.Join(
        ' ',
        Path.GetFileNameWithoutExtension(fileName)
            .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries));
}
