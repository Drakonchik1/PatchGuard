using PatchGuard.Models;

namespace PatchGuard.Services.Fixes;

public interface IGuidedFixPlanService
{
    GuidedFixPlan? TryBuildFromFinding(Finding finding, string? linkedScanScenario = null);

    GuidedFixPlan? TryBuildFromFixStep(FixStep step, string? linkedScanScenario = null);

    bool CanBuildFromFinding(Finding finding) =>
        TryBuildFromFinding(finding) is not null;

    bool CanBuildFromFixStep(FixStep step) =>
        TryBuildFromFixStep(step) is not null;

    GuidedFixPreview Preview(GuidedFixPlan plan);

    /// <summary>
    /// Safety gate: requires <see cref="GuidedFixConfirmation.IsConfirmed"/>.
    /// Never runs privileged/optional optimizer steps (e.g. Explorer restart).
    /// </summary>
    Task<GuidedFixRunResult> ExecuteAsync(
        GuidedFixPlan plan,
        GuidedFixConfirmation confirmation,
        IProgress<OptimizationStepResult>? progress = null,
        CancellationToken cancellationToken = default);
}
