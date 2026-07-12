using PatchGuard.Models;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

public sealed class HealthScorePolicyTests
{
    private readonly HealthScorePolicy _policy = new();

    [Fact]
    public void EmptyFindingsHavePerfectScore() =>
        Assert.Equal(100, _policy.Calculate([]));

    [Fact]
    public void RepeatedEventLogNoiseIsCappedPerModule()
    {
        var findings = Enumerable.Range(1, 30)
            .Select(index => FindingFor(
                "Event logs",
                FindingSeverity.Warning,
                FindingRisk.Medium,
                $"Event {index}"))
            .ToArray();

        Assert.Equal(70, _policy.Calculate(findings));
    }

    [Fact]
    public void DuplicateFindingsNeverCostMoreThanTheirModuleCap()
    {
        var warnings = Enumerable.Repeat(
            FindingFor("Disk space", FindingSeverity.Warning, FindingRisk.High),
            100).ToArray();

        Assert.Equal(70, _policy.Calculate(warnings));
    }

    [Fact]
    public void IndependentModuleRisksCombine()
    {
        var findings = new[]
        {
            FindingFor("Disk space", FindingSeverity.Warning, FindingRisk.High),
            FindingFor("CPU load", FindingSeverity.Warning, FindingRisk.Low)
        };

        Assert.Equal(77, _policy.Calculate(findings));
    }

    [Fact]
    public void OverallPenaltyHasDeterministicFloor()
    {
        var findings = Enumerable.Range(1, 20)
            .Select(index => FindingFor(
                $"Module {index}",
                FindingSeverity.Critical,
                FindingRisk.High))
            .ToArray();

        Assert.Equal(20, _policy.Calculate(findings));
    }

    [Theory]
    [InlineData(FindingSeverity.Info, FindingRisk.High, 100)]
    [InlineData(FindingSeverity.Warning, FindingRisk.NotApplicable, 100)]
    [InlineData(FindingSeverity.Warning, FindingRisk.Low, 92)]
    [InlineData(FindingSeverity.Warning, FindingRisk.Medium, 88)]
    [InlineData(FindingSeverity.Warning, FindingRisk.High, 85)]
    [InlineData(FindingSeverity.Critical, FindingRisk.High, 75)]
    public void SeverityAndRiskDefineBasePenalty(
        FindingSeverity severity,
        FindingRisk risk,
        int expectedScore) =>
        Assert.Equal(expectedScore, _policy.Calculate([FindingFor("Test", severity, risk)]));

    private static Finding FindingFor(
        string module,
        FindingSeverity severity,
        FindingRisk risk,
        string title = "Finding") =>
        new()
        {
            ModuleName = module,
            Title = title,
            Details = "Measured evidence",
            Severity = severity,
            Risk = risk
        };
}
