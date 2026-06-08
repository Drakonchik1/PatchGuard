using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;

namespace PatchGuard.Services.Performance;

public sealed class GameFpsService : IGameFpsService
{
    private readonly PatchGuardDbContext _db;

    public GameFpsService(PatchGuardDbContext db)
    {
        _db = db;
    }

    public async Task<GameFpsCompareResult> SaveAndCompareAsync(
        string gameName,
        int fps,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = gameName.Trim();
        var previous = await _db.GameFpsEntries
            .Where(e => e.GameName == normalized)
            .OrderByDescending(e => e.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        _db.GameFpsEntries.Add(new GameFpsEntry
        {
            GameName = normalized,
            Fps = fps,
            RecordedAt = DateTime.UtcNow,
            Note = note
        });
        await _db.SaveChangesAsync(cancellationToken);

        return BuildResult(fps, previous?.Fps);
    }

    public async Task<GameFpsCompareResult?> GetLatestAsync(
        string gameName,
        CancellationToken cancellationToken = default)
    {
        var latest = await _db.GameFpsEntries
            .Where(e => e.GameName == gameName.Trim())
            .OrderByDescending(e => e.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return null;
        }

        var previous = await _db.GameFpsEntries
            .Where(e => e.GameName == gameName.Trim() && e.Id != latest.Id)
            .OrderByDescending(e => e.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return BuildResult(latest.Fps, previous?.Fps);
    }

    private static GameFpsCompareResult BuildResult(int current, int? previous)
    {
        if (previous is null)
        {
            return new GameFpsCompareResult
            {
                CurrentFps = current,
                Summary = $"{current} FPS — first entry for this game."
            };
        }

        var delta = current - previous.Value;
        var pct = previous.Value > 0 ? delta * 100.0 / previous.Value : 0;
        var summary = delta switch
        {
            > 0 => $"{current} FPS (was {previous}, +{delta}, +{pct:F0}%)",
            < 0 => $"{current} FPS (was {previous}, {delta}, {pct:F0}%)",
            _ => $"{current} FPS — unchanged vs last log."
        };

        return new GameFpsCompareResult
        {
            CurrentFps = current,
            PreviousFps = previous,
            Summary = summary
        };
    }
}
