using System.ServiceProcess;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public static class UpdateServiceHealthEvaluator
{
    public static Finding CreateFinding(
        string serviceName,
        ServiceControllerStatus status,
        ServiceStartMode startMode)
    {
        var isRunning = status == ServiceControllerStatus.Running;
        var isLegitimateOnDemandStop =
            status == ServiceControllerStatus.Stopped &&
            startMode == ServiceStartMode.Manual;
        var isHealthy = isRunning || isLegitimateOnDemandStop;

        return new Finding
        {
            ModuleName = "Update services",
            Title = $"{serviceName}: {status}",
            Details = isRunning
                ? "Service is running."
                : isLegitimateOnDemandStop
                    ? "Service is stopped and configured for manual or trigger-start operation; this is a normal on-demand state."
                    : "Service state may prevent Windows Update from operating normally.",
            Severity = isHealthy ? FindingSeverity.Info : FindingSeverity.Warning,
            Evidence = $"Windows Service Control Manager reported {serviceName} status as {status} with {startMode} startup.",
            Recommendation = isHealthy
                ? null
                : "Open services.msc, inspect the service configuration, and ask an administrator to restore the expected Windows configuration.",
            ActionState = isHealthy ? FindingActionState.None : FindingActionState.Recommended,
            AdminRequirement = isHealthy
                ? FindingAdminRequirement.NotRequired
                : FindingAdminRequirement.Required,
            Risk = isHealthy ? FindingRisk.NotApplicable : FindingRisk.Medium,
            VerificationStatus = isHealthy
                ? FindingVerificationStatus.NotRequired
                : FindingVerificationStatus.NotVerified
        };
    }
}
