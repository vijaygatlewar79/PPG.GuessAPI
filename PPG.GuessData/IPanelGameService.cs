using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IPanelGameService
{
    Task<IReadOnlyList<PanelGame>> GetAvailableGamesAsync(
        string filesDirectory,
        CancellationToken cancellationToken = default);

    string ResolveGameFilePath(string filesDirectory, string? fileName);
}
