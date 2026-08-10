using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class ScanMetricBuilder
{
    public static IReadOnlyList<ScanMetric> FromFindings(IReadOnlyList<Finding> findings)
    {
        var metrics = new List<ScanMetric>();

        var os = findings.FirstOrDefault(f => f.ModuleName == "Operating system");
        if (os is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "OS build",
                Value = Trim(os.Title, 48),
                BarPercent = 100,
                Severity = FindingSeverity.Info,
                ShowProgressBar = false
            });
        }

        var disk = findings.FirstOrDefault(f => f.ModuleName == "Disk space");
        if (disk is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Storage",
                Value = Trim(disk.Title.Replace("C: has ", string.Empty), 48),
                BarPercent = disk.Severity >= FindingSeverity.Warning ? 35 : 85,
                Severity = disk.Severity,
                ShowProgressBar = true
            });
        }

        var memory = findings.FirstOrDefault(f => f.ModuleName == "Memory");
        if (memory is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Memory",
                Value = Trim(memory.Title.Replace("RAM ", string.Empty), 48),
                BarPercent = memory.Severity >= FindingSeverity.Warning ? 92 : 55,
                Severity = memory.Severity,
                ShowProgressBar = true
            });
        }

        foreach (var temp in findings.Where(f => f.ModuleName == "Temperatures" && f.Title.Contains("temperature")))
        {
            metrics.Add(new ScanMetric
            {
                Label = temp.Title.StartsWith("GPU") ? "GPU temp" : "CPU temp",
                Value = Trim(temp.Title.Replace(" temperature", string.Empty)
                    .Replace("CPU ", string.Empty).Replace("GPU ", string.Empty), 24),
                BarPercent = temp.Severity == FindingSeverity.Critical ? 95 : temp.Severity == FindingSeverity.Warning ? 80 : 45,
                Severity = temp.Severity,
                ShowProgressBar = true
            });
        }

        var gpu = findings.FirstOrDefault(f => f.ModuleName == "Graphics card");
        if (gpu is not null)
        {
            metrics.Add(new ScanMetric
            {
                Label = "GPU",
                Value = Trim(gpu.Title, 48),
                BarPercent = 100,
                Severity = FindingSeverity.Info,
                ShowProgressBar = false
            });
        }

        var services = findings.Where(f => f.ModuleName == "Update services" && f.Severity >= FindingSeverity.Warning).ToList();
        if (services.Count > 0)
        {
            metrics.Add(new ScanMetric
            {
                Label = "Update services",
                Value = $"{services.Count} issue(s)",
                BarPercent = 40,
                Severity = FindingSeverity.Warning,
                ShowProgressBar = false
            });
        }

        return metrics;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
