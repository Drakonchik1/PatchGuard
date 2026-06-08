using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class ScanMetricBuilder
{
    public static IReadOnlyList<ScanMetric> FromFindings(IReadOnlyList<Finding> findings)
    {
        var metrics = new List<ScanMetric>
        {
            new()
            {
                Label = "Findings",
                Value = findings.Count.ToString(),
                BarPercent = Math.Min(100, findings.Count * 8),
                Severity = FindingSeverity.Info
            },
            new()
            {
                Label = "Warnings",
                Value = findings.Count(f => f.Severity == FindingSeverity.Warning).ToString(),
                BarPercent = Math.Min(100, findings.Count(f => f.Severity == FindingSeverity.Warning) * 25),
                Severity = FindingSeverity.Warning
            },
            new()
            {
                Label = "Critical",
                Value = findings.Count(f => f.Severity == FindingSeverity.Critical).ToString(),
                BarPercent = Math.Min(100, findings.Count(f => f.Severity == FindingSeverity.Critical) * 40),
                Severity = FindingSeverity.Critical
            }
        };

        var os = findings.FirstOrDefault(f => f.ModuleName == "Operating system");
        if (os is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "OS build",
                Value = Trim(os.Title, 28),
                BarPercent = 100,
                Severity = FindingSeverity.Info
            });
        }

        var disk = findings.FirstOrDefault(f => f.ModuleName == "Disk space");
        if (disk is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Storage",
                Value = Trim(disk.Title.Replace("C: has ", string.Empty), 24),
                BarPercent = disk.Severity >= FindingSeverity.Warning ? 35 : 85,
                Severity = disk.Severity
            });
        }

        var services = findings.Where(f => f.ModuleName == "Update services" && f.Severity >= FindingSeverity.Warning).ToList();
        if (services.Count > 0)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Update svc",
                Value = $"{services.Count} issue(s)",
                BarPercent = 40,
                Severity = FindingSeverity.Warning
            });
        }

        return metrics;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
