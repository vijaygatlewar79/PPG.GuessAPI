using PPG.GuessData.Models;

namespace PPG.GuessData;

public interface IPanelGameService
{
    Task<IReadOnlyList<PanelGame>> GetAvailableGamesAsync(
        CancellationToken cancellationToken = default);

    Task<string> ResolveGameFileNameAsync(
        string? fileName,
        CancellationToken cancellationToken = default);
}
