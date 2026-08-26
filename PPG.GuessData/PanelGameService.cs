using PPG.GuessData.Models;

namespace PPG.GuessData;

public sealed class PanelGameService : IPanelGameService
{
    private readonly IExcelReaderService _excelReaderService;
    private readonly IPanelFileStorage _fileStorage;

    public PanelGameService(
        IExcelReaderService excelReaderService,
        IPanelFileStorage fileStorage)
    {
        _excelReaderService = excelReaderService;
        _fileStorage = fileStorage;
    }

    public async Task<IReadOnlyList<PanelGame>> GetAvailableGamesAsync(
        CancellationToken cancellationToken = default)
    {
        var games = new List<PanelGame>();
        var fileNames = await _fileStorage.ListExcelFileNamesAsync(cancellationToken);
        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? sourceUrl;
            try
            {
                await using var workbookStream = await _fileStorage.OpenExcelFileAsync(
                    fileName,
                    cancellationToken);
                sourceUrl = await _excelReaderService.ReadSourceUrlAsync(
                    workbookStream,
                    cancellationToken);
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

    public async Task<string> ResolveGameFileNameAsync(
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var gameFileNames = await _fileStorage.ListExcelFileNamesAsync(cancellationToken);
        if (gameFileNames.Count == 0)
        {
            throw new FileNotFoundException(
                "No supported Excel game files were found in Azure Blob Storage.");
        }

        var requestedFileName = fileName?.Trim();
        var gameFileName = string.IsNullOrWhiteSpace(requestedFileName)
            ? gameFileNames
                .OrderBy(BuildDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
                .First()
            : gameFileNames.FirstOrDefault(candidate => string.Equals(
                  candidate,
                  requestedFileName,
                  StringComparison.Ordinal))
              ?? gameFileNames.FirstOrDefault(candidate => string.Equals(
                  candidate,
                  requestedFileName,
                  StringComparison.OrdinalIgnoreCase));

        if (gameFileName is null)
        {
            throw new ArgumentException("Select a valid game file.", nameof(fileName));
        }

        return gameFileName;
    }

    private static string BuildDisplayName(string fileName) => string.Join(
        ' ',
        Path.GetFileNameWithoutExtension(fileName)
            .Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries));
}
