namespace PatchGuard.Models;

public enum GuidedFixActionKind
{
    OptimizationStep = 0,
    LaunchUri = 1
}

public enum GuidedFixOutcome
{
    RejectedNotConfirmed = 0,
    Cancelled = 1,
    Failed = 2,
    PartialFailure = 3,
    Succeeded = 4
}

public sealed class GuidedFixPlanStep
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required GuidedFixActionKind Kind { get; init; }
    public string? OptimizationStepName { get; init; }
    public string? LaunchUri { get; init; }
    public FindingRisk Risk { get; init; } = FindingRisk.Low;
    public FindingAdminRequirement AdminRequirement { get; init; } = FindingAdminRequirement.NotRequired;
}

public sealed class GuidedFixPlan
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Source { get; init; }
    public required IReadOnlyList<GuidedFixPlanStep> Steps { get; init; }
    public string? LinkedScanScenario { get; init; }

    public FindingRisk OverallRisk =>
        Steps.Count == 0
            ? FindingRisk.NotApplicable
            : Steps.Max(s => s.Risk);

    public FindingAdminRequirement AdminRequirement =>
        Steps.Any(s => s.AdminRequirement == FindingAdminRequirement.Required)
            ? FindingAdminRequirement.Required
            : FindingAdminRequirement.NotRequired;
}

public sealed class GuidedFixPreview
{
    public required GuidedFixPlan Plan { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> StepSummaries { get; init; }
    public FindingRisk Risk { get; init; }
    public FindingAdminRequirement AdminRequirement { get; init; }
}

public readonly record struct GuidedFixConfirmation(bool IsConfirmed, DateTimeOffset ConfirmedAtUtc)
{
    public static GuidedFixConfirmation Rejected { get; } = new(false, default);

    public static GuidedFixConfirmation ConfirmNow() =>
        new(true, DateTimeOffset.UtcNow);
}

public sealed class GuidedFixRunResult
{
    public required GuidedFixOutcome Outcome { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<OptimizationStepResult> StepResults { get; init; } = [];
    public bool Verified { get; init; }
    public long BytesFreed { get; init; }
}
