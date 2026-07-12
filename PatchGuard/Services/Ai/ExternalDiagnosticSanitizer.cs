using System.Text;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class ExternalDiagnosticSanitizer
{
    private static readonly string[] KnownCategories =
    [
        "CPU load",
        "Disk space",
        "Event Log",
        "Event logs",
        "Graphics card",
        "Memory",
        "Operating system",
        "Temperatures",
        "Update services",
        "Windows Update history"
    ];

    public static string BuildContext(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Scenario: {scenario.GetTitle()}");
        foreach (var finding in findings)
        {
            builder.AppendLine(
                $"- [{finding.Severity}] {SanitizeCategory(finding.ModuleName)}");
        }

        builder.AppendLine(
            "Finding titles, free-form event text, machine identifiers, paths, and possible credentials were omitted locally.");
        return builder.ToString();
    }

    public static IReadOnlyList<string> BuildSearchQueries(
        IReadOnlyList<Finding> findings)
    {
        var categories = findings
            .Where(finding => finding.Severity >= FindingSeverity.Warning)
            .Select(finding => SanitizeCategory(finding.ModuleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (categories.Count == 0)
        {
            categories.Add("Windows system health");
        }

        return categories
            .Select(category => $"Windows 11 {category} troubleshooting")
            .ToList();
    }

    private static string SanitizeCategory(string value)
    {
        var trimmed = value.Trim();
        foreach (var known in KnownCategories)
        {
            if (string.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return "Windows diagnostic";
    }
}
