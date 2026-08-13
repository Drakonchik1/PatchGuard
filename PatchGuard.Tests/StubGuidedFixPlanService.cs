using PatchGuard.Models;
using PatchGuard.Services.Fixes;

namespace PatchGuard.Tests;

internal sealed class StubGuidedFixPlanService : IGuidedFixPlanService
{
    public GuidedFixPlan? TryBuildFromFinding(Finding finding, string? linkedScanScenario = null) => null;

    public GuidedFixPlan? TryBuildFromFixStep(FixStep step, string? linkedScanScenario = null) => null;

    public GuidedFixPreview Preview(GuidedFixPlan plan) =>
        new()
        {
            Plan = plan,
            Summary = "stub",
            StepSummaries = [],
            Risk = FindingRisk.Low,
            AdminRequirement = FindingAdminRequirement.NotRequired
        };

    public Task<GuidedFixRunResult> ExecuteAsync(
        GuidedFixPlan plan,
        GuidedFixConfirmation confirmation,
        IProgress<OptimizationStepResult>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GuidedFixRunResult
        {
            Outcome = GuidedFixOutcome.RejectedNotConfirmed,
            Summary = "stub"
        });
}
