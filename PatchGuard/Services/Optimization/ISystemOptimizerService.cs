using PatchGuard.Models;

namespace PatchGuard.Services.Optimization;

public sealed class OptimizationRunSummary
{
    public List<OptimizationStepResult> Steps { get; } = [];
    public long TotalBytesFreed => Steps.Sum(s => s.BytesFreed);
    public int SucceededCount => Steps.Count(s => s.Status == OptimizationStatus.Success);
    public DateTime CompletedAt { get; init; } = DateTime.Now;
}

public interface ISystemOptimizerService
{
    /// <summary>Names/descriptions of the steps that will run (for preview in the UI).</summary>
    IReadOnlyList<IOptimizationStep> GetSteps(bool includeOptional);

    /// <summary>
    /// Runs the safe optimization steps in order, reporting each result as it
    /// completes. Only safe, reversible actions are taken; no Windows settings
    /// are modified.
    /// </summary>
    Task<OptimizationRunSummary> RunAsync(
        bool includeOptional,
        IProgress<OptimizationStepResult> progress,
        CancellationToken cancellationToken = default);
}
