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

        var memory = findings.FirstOrDefault(f => f.ModuleName == "Memory");
        if (memory is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Memory",
                Value = Trim(memory.Title.Replace("RAM ", string.Empty), 24),
                BarPercent = memory.Severity >= FindingSeverity.Warning ? 92 : 55,
                Severity = memory.Severity
            });
        }

        foreach (var temp in findings.Where(f => f.ModuleName == "Temperatures" && f.Title.Contains("temperature")))
        {
            metrics.Add(new ScanMetric
            {
                Label = temp.Title.StartsWith("GPU") ? "GPU temp" : "CPU temp",
                Value = Trim(temp.Title.Replace(" temperature", string.Empty)
                    .Replace("CPU ", string.Empty).Replace("GPU ", string.Empty), 12),
                BarPercent = temp.Severity == FindingSeverity.Critical ? 95 : temp.Severity == FindingSeverity.Warning ? 80 : 45,
                Severity = temp.Severity
            });
        }

        var gpu = findings.FirstOrDefault(f => f.ModuleName == "Graphics card");
        if (gpu is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "GPU",
                Value = Trim(gpu.Title, 26),
                BarPercent = 100,
                Severity = FindingSeverity.Info
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
