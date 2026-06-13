using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Optimization;

namespace PatchGuard.Services.History;

public sealed class PerformanceHistoryService : IPerformanceHistoryService
{
    private readonly PatchGuardDbContext _dbContext;

    public PerformanceHistoryService(PatchGuardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveFpsAsync(FpsCaptureResult result, CancellationToken cancellationToken = default)
    {
        if (!result.Success)
        {
            return;
        }

        _dbContext.FpsCaptures.Add(new FpsCaptureRecord
        {
            ProcessName = result.ProcessName,
            AverageFps = result.AverageFps,
            OnePercentLowFps = result.OnePercentLowFps,
            PointOnePercentLowFps = result.PointOnePercentLowFps,
            FrameCount = result.FrameCount,
            CapturedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FpsCaptureRecord>> GetRecentFpsAsync(int take = 8, CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.FpsCaptures
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
        _dbContext.OptimizationRuns.Add(new OptimizationRunRecord
        {
            RanAt = DateTime.UtcNow,
            BytesFreed = summary.TotalBytesFreed,
            StepsSucceeded = summary.SucceededCount,
            Summary = $"{summary.SucceededCount} step(s), {OptimizationStepResult.FormatBytes(summary.TotalBytesFreed)} freed"
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OptimizationRunRecord>> GetRecentOptimizationsAsync(int take = 8, CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.OptimizationRuns
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
