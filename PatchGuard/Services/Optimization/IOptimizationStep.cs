using PatchGuard.Models;

namespace PatchGuard.Services.Optimization;

public interface IOptimizationStep
{
    string Name { get; }
    string Description { get; }

    /// <summary>Optional steps run only when the user explicitly opts in.</summary>
    bool IsOptional { get; }

    Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default);
}
