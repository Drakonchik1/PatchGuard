using PatchGuard.Models;

namespace PatchGuard.Services.Optimization;

public interface IOptimizationStep
{
    string Name { get; }
    string Description { get; }

    /// <summary>Optional steps run only when the user explicitly opts in.</summary>
    bool IsOptional { get; }

    /// <summary>
    /// True when invocation performs synchronous filesystem, process, or native
    /// work and therefore needs a worker-thread boundary.
    /// </summary>
    bool RunsBlockingWork => true;

    Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default);
}
