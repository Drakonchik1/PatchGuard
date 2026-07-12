using PatchGuard.Models;

namespace PatchGuard.Services.Health;

public interface IHealthScorePolicy
{
    string Version { get; }
    int Calculate(IReadOnlyList<Finding> findings);
}

public sealed class HealthScorePolicy : IHealthScorePolicy
{
    private const int MaximumModulePenalty = 30;
    private const int MaximumOverallPenalty = 80;

    public string Version => "risk-capped-v1";

    public int Calculate(IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var penalty = findings
            .GroupBy(finding => finding.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Sum(group => CalculateModulePenalty(group.Select(CalculateFindingPenalty)));

        return 100 - Math.Min(penalty, MaximumOverallPenalty);
    }

    private static int CalculateModulePenalty(IEnumerable<int> findingPenalties)
    {
        var penalties = findingPenalties
            .Where(penalty => penalty > 0)
            .OrderByDescending(penalty => penalty)
            .ToArray();
        if (penalties.Length == 0)
        {
            return 0;
        }

        var duplicatePenalty = penalties
            .Skip(1)
            .Sum(penalty => (int)Math.Ceiling(penalty * 0.25));
        return Math.Min(penalties[0] + duplicatePenalty, MaximumModulePenalty);
    }

    private static int CalculateFindingPenalty(Finding finding) =>
        finding.Severity switch
        {
            FindingSeverity.Critical => finding.Risk == FindingRisk.High ? 25 : 20,
            FindingSeverity.Warning => finding.Risk switch
            {
                FindingRisk.NotApplicable => 0,
                FindingRisk.Low => 8,
                FindingRisk.Medium => 12,
                FindingRisk.High => 15,
                _ => 10
            },
            _ => 0
        };
}
