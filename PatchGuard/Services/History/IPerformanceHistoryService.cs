using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Optimization;

namespace PatchGuard.Services.History;

public interface IPerformanceHistoryService
{
    Task SaveFpsAsync(FpsCaptureResult result, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FpsCaptureRecord>> GetRecentFpsAsync(int take = 8, CancellationToken cancellationToken = default);

    Task SaveOptimizationAsync(OptimizationRunSummary summary, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OptimizationRunRecord>> GetRecentOptimizationsAsync(int take = 8, CancellationToken cancellationToken = default);
}
