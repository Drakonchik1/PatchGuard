using System.ServiceProcess;
using PatchGuard.Models;
using PatchGuard.Services.Diagnostics;

namespace PatchGuard.Tests;

public sealed class UpdateServiceHealthTests
{
    [Theory]
    [InlineData(ServiceControllerStatus.Running, ServiceStartMode.Automatic, FindingSeverity.Info, FindingActionState.None)]
    [InlineData(ServiceControllerStatus.Stopped, ServiceStartMode.Manual, FindingSeverity.Info, FindingActionState.None)]
    [InlineData(ServiceControllerStatus.Stopped, ServiceStartMode.Automatic, FindingSeverity.Warning, FindingActionState.Recommended)]
    [InlineData(ServiceControllerStatus.Paused, ServiceStartMode.Automatic, FindingSeverity.Warning, FindingActionState.Recommended)]
    public void ServiceStateAndStartModeDetermineHealth(
        ServiceControllerStatus status,
        ServiceStartMode startMode,
        FindingSeverity expectedSeverity,
        FindingActionState expectedAction)
    {
        var finding = UpdateServiceHealthEvaluator.CreateFinding(
            "BITS",
            status,
            startMode);

        Assert.Equal(expectedSeverity, finding.Severity);
        Assert.Equal(expectedAction, finding.ActionState);
        Assert.Contains(startMode.ToString(), finding.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingFactoryProducesCompleteUnavailableMetadata()
    {
        var finding = FindingFactory.Unavailable(
            "Test module",
            "Test unavailable",
            "Query failed.");

        Assert.Equal(FindingActionState.Unavailable, finding.ActionState);
        Assert.Equal(FindingAdminRequirement.Unknown, finding.AdminRequirement);
        Assert.Equal(FindingRisk.Unknown, finding.Risk);
        Assert.Equal(FindingVerificationStatus.NotVerified, finding.VerificationStatus);
        Assert.False(string.IsNullOrWhiteSpace(finding.Evidence));
    }
}
