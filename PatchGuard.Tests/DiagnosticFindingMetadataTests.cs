using PatchGuard.Models;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Tests;

public sealed class DiagnosticFindingMetadataTests
{
    [Fact]
    public async Task HighCpuFindingUsesMeasuredEvidenceAndSafeUserAction()
    {
        var module = new CpuLoadDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
        {
            CpuName = "Test CPU",
            CpuLoadPercent = 95,
            CpuClockMhz = 4200
        }));

        var finding = Assert.Single(await module.RunAsync());

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Contains("95", finding.Evidence, StringComparison.Ordinal);
        Assert.Contains("4200", finding.Evidence, StringComparison.Ordinal);
        Assert.Equal(FindingAdminRequirement.NotRequired, finding.AdminRequirement);
        Assert.Equal(FindingRisk.Low, finding.Risk);
        Assert.NotNull(finding.Recommendation);
        Assert.Equal(FindingVerificationStatus.NotVerified, finding.VerificationStatus);
    }

    [Fact]
    public async Task NormalCpuFindingIsEvidenceOnlyNotActionable()
    {
        var module = new CpuLoadDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
        {
            CpuName = "Test CPU",
            CpuLoadPercent = 20
        }));

        var finding = Assert.Single(await module.RunAsync());

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Null(finding.Recommendation);
        Assert.Contains("No corrective action", finding.RecommendedFix, StringComparison.Ordinal);
        Assert.Equal(FindingAdminRequirement.NotRequired, finding.AdminRequirement);
        Assert.NotEqual(FindingRisk.Unknown, finding.Risk);
    }

    [Fact]
    public async Task LimitedTemperatureSensorsDescribeRequiredElevation()
    {
        var module = new TemperatureDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
        {
            SensorsLimited = true
        }));

        var finding = Assert.Single(await module.RunAsync());

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("administrator", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FindingAdminRequirement.Required, finding.AdminRequirement);
        Assert.Equal(FindingRisk.Low, finding.Risk);
        Assert.NotNull(finding.Recommendation);
    }

    [Fact]
    public async Task UnavailableMemoryReadingDoesNotPretendToOfferAFix()
    {
        var module = new MemoryLoadDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot()));

        var finding = Assert.Single(await module.RunAsync());

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Contains("unavailable", finding.Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.Null(finding.Recommendation);
        Assert.Equal(FindingAdminRequirement.Unknown, finding.AdminRequirement);
        Assert.Equal(FindingRisk.Unknown, finding.Risk);
    }

    [Fact]
    public void FindingExposesTypedActionState()
    {
        var property = typeof(Finding).GetProperty("ActionState");

        Assert.NotNull(property);
        Assert.Equal("FindingActionState", property.PropertyType.Name);
    }

    [Theory]
    [MemberData(nameof(BehavioralFindingCases))]
    public void DiagnosticResultsExposeCompleteBehavioralMetadata(Finding finding)
    {
        Assert.False(string.IsNullOrWhiteSpace(finding.ModuleName));
        Assert.False(string.IsNullOrWhiteSpace(finding.Title));
        Assert.False(string.IsNullOrWhiteSpace(finding.Evidence));
        Assert.NotEqual(FindingRisk.Unknown, finding.Risk);
        Assert.NotEqual(FindingAdminRequirement.Unknown, finding.AdminRequirement);
        Assert.True(
            finding.ActionState != FindingActionState.Recommended ||
            !string.IsNullOrWhiteSpace(finding.Recommendation));
    }

    public static IEnumerable<object[]> BehavioralFindingCases()
    {
        var modules = new IDiagnosticModule[]
        {
            new CpuLoadDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
            {
                CpuLoadPercent = 90,
                CpuClockMhz = 4000
            })),
            new MemoryLoadDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
            {
                RamLoadPercent = 50,
                RamUsedGb = 8,
                RamTotalGb = 16
            })),
            new TemperatureDiagnosticModule(new StubHardwareMonitor(new HardwareSnapshot
            {
                CpuTemperatureC = 55
            }))
        };

        foreach (var module in modules)
        {
            foreach (var finding in module.RunAsync().GetAwaiter().GetResult())
            {
                yield return [finding];
            }
        }
    }

    private sealed class StubHardwareMonitor(HardwareSnapshot snapshot) : IHardwareMonitorService
    {
        public HardwareSnapshot Capture() => snapshot;
        public void Dispose() { }
    }
}
