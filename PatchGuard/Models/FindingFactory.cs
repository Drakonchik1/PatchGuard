namespace PatchGuard.Models;

public static class FindingFactory
{
    public static Finding Unavailable(
        string moduleName,
        string title,
        string evidence,
        FindingSeverity severity = FindingSeverity.Warning) =>
        new()
        {
            ModuleName = moduleName,
            Title = title,
            Details = "The diagnostic result is unavailable.",
            Severity = severity,
            Evidence = evidence,
            ActionState = FindingActionState.Unavailable,
            AdminRequirement = FindingAdminRequirement.Unknown,
            Risk = FindingRisk.Unknown,
            VerificationStatus = FindingVerificationStatus.NotVerified
        };
}
