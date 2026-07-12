using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Optimization;

namespace PatchGuard.Services.History;

public sealed class PerformanceHistoryService : IPerformanceHistoryService
{
    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;

    public PerformanceHistoryService(IDbContextFactory<PatchGuardDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task SaveFpsAsync(FpsCaptureResult result, CancellationToken cancellationToken = default)
    {
        if (!result.Success)
        {
            return;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.FpsCaptures.Add(new FpsCaptureRecord
        {
            ProcessName = result.ProcessName,
            AverageFps = result.AverageFps,
            OnePercentLowFps = result.OnePercentLowFps,
            PointOnePercentLowFps = result.PointOnePercentLowFps,
            FrameCount = result.FrameCount,
            CapturedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FpsCaptureRecord>> GetRecentFpsAsync(int take = 8, CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.FpsCaptures
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

    public async Task SaveOptimizationAsync(OptimizationRunSummary summary, CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.OptimizationRuns.Add(new OptimizationRunRecord
        {
            RanAt = DateTime.UtcNow,
            BytesFreed = summary.TotalBytesFreed,
            StepsSucceeded = summary.SucceededCount,
            Summary = $"{summary.SucceededCount} step(s), {OptimizationStepResult.FormatBytes(summary.TotalBytesFreed)} freed"
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OptimizationRunRecord>> GetRecentOptimizationsAsync(int take = 8, CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.OptimizationRuns
            .AsNoTracking()
            .OrderByDescending(r => r.RanAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            record.RanAt = record.RanAt.ToLocalTime();
        }

        return records;
    }
}
