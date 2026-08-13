using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.History;

public sealed class SensorHistoryService : ISensorHistoryService
{
    /// <summary>Rolling window kept in SQLite for alerts / future ML.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;
    private readonly TimeSpan _retention;

    public SensorHistoryService(
        IDbContextFactory<PatchGuardDbContext> dbContextFactory,
        TimeSpan? retention = null)
    {
        _dbContextFactory = dbContextFactory;
        _retention = retention ?? DefaultRetention;
    }

    public async Task SaveSnapshotAsync(
        HardwareSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.SensorSnapshots.Add(new SensorSnapshotRecord
        {
            CapturedAt = DateTime.UtcNow,
            CpuTemperatureC = snapshot.CpuTemperatureC,
            CpuLoadPercent = snapshot.CpuLoadPercent,
            GpuTemperatureC = snapshot.GpuTemperatureC,
            GpuLoadPercent = snapshot.GpuLoadPercent,
            RamLoadPercent = snapshot.RamLoadPercent,
            RamUsedGb = snapshot.RamUsedGb
        });

        var cutoff = DateTime.UtcNow - _retention;
        await dbContext.SensorSnapshots
            .Where(r => r.CapturedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.SensorSnapshots
            .AsNoTracking()
            .OrderByDescending(r => r.CapturedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            record.CapturedAt = record.CapturedAt.ToLocalTime();
        }

        return records;
    }

    public async Task<SensorSnapshotRecord?> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var recent = await GetRecentAsync(1, cancellationToken);
        return recent.Count > 0 ? recent[0] : null;
    }
}
