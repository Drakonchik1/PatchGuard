using PatchGuard.Models;

namespace PatchGuard.Services.Optimization;

public sealed class SystemOptimizerService : ISystemOptimizerService
{
    private readonly IReadOnlyList<IOptimizationStep> _steps;

    public SystemOptimizerService(IEnumerable<IOptimizationStep> steps)
    {
        // Preserve DI registration order so the run sequence is deterministic.
        _steps = steps.ToList();
    }

    public IReadOnlyList<IOptimizationStep> GetSteps(bool includeOptional) =>
        _steps.Where(s => includeOptional || !s.IsOptional).ToList();

    public async Task<OptimizationRunSummary> RunAsync(
        bool includeOptional,
        IProgress<OptimizationStepResult> progress,
        CancellationToken cancellationToken = default)
    {
        var summary = new OptimizationRunSummary();

        foreach (var step in GetSteps(includeOptional))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var running = new OptimizationStepResult
            {
                StepName = step.Name,
                Status = OptimizationStatus.Running,
                Detail = step.Description
            };
            progress.Report(running);

            OptimizationStepResult result;
            try
            {
                result = await step.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new OptimizationStepResult
                {
                    StepName = step.Name,
                    Status = OptimizationStatus.Failed,
                    Detail = ex.Message
                };
            }

            summary.Steps.Add(result);
            progress.Report(result);
        }

        return summary;
    }
}
