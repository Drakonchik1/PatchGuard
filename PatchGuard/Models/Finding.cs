namespace PatchGuard.Models;

public sealed class Finding
{
    public required string ModuleName { get; init; }
    public required string Title { get; init; }
    public required string Details { get; init; }
    public FindingSeverity Severity { get; init; } = FindingSeverity.Info;
    public string? Recommendation { get; init; }
    public string Explanation => Details;
    public string Evidence { get; init; } =
        "Evidence unavailable: the diagnostic module did not provide a measurement.";
    public string RecommendedFix =>
        ActionState switch
        {
            FindingActionState.None => "No corrective action is recommended for this result.",
            FindingActionState.Recommended when !string.IsNullOrWhiteSpace(Recommendation) => Recommendation,
            FindingActionState.Recommended => "A corrective action is recommended, but instructions are unavailable.",
            _ => "No reliable corrective action is available from this result."
        };
    public FindingActionState ActionState { get; init; } = FindingActionState.Unavailable;
    public FindingAdminRequirement AdminRequirement { get; init; } = FindingAdminRequirement.Unknown;
    public FindingRisk Risk { get; init; } = FindingRisk.Unknown;
    public FindingVerificationStatus VerificationStatus { get; init; } = FindingVerificationStatus.NotVerified;
}

public enum FindingActionState
{
    None,
    Recommended,
    Unavailable
}

public enum FindingAdminRequirement
{
    Unknown,
    NotRequired,
    Required
}

public enum FindingRisk
{
    Unknown,
    NotApplicable,
    Low,
    Medium,
    High
}

public enum FindingVerificationStatus
{
    NotVerified,
    NotRequired,
    Verified,
    VerificationFailed
}
