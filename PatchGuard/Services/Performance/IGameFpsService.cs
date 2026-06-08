namespace PatchGuard.Services.Performance;

public sealed class GameFpsCompareResult
{
    public required int CurrentFps { get; init; }
    public int? PreviousFps { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public interface IGameFpsService
{
    Task<GameFpsCompareResult> SaveAndCompareAsync(
        string gameName,
        int fps,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<GameFpsCompareResult?> GetLatestAsync(string gameName, CancellationToken cancellationToken = default);
}
