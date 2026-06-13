using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Services.Diagnostics;

public sealed class MemoryLoadDiagnosticModule : IDiagnosticModule
{
    private const double HighUsagePercent = 90;
    private const double LowAvailableGb = 1.5;

    private readonly IHardwareMonitorService _hardware;

    public MemoryLoadDiagnosticModule(IHardwareMonitorService hardware)
    {
        _hardware = hardware;
    }

    public string Name => "Memory";
    public string Description => "Checks installed and in-use system memory (RAM).";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var snapshot = _hardware.Capture();

        if (snapshot is not { RamTotalGb: { } total, RamUsedGb: { } used } || total <= 0)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Memory usage unavailable",
                Details = "Could not read system memory information.",
                Severity = FindingSeverity.Info
            });
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var available = Math.Max(0, total - used);
        var percent = snapshot.RamLoadPercent ?? used / total * 100;
        var severity = percent >= HighUsagePercent || available <= LowAvailableGb
            ? FindingSeverity.Warning
            : FindingSeverity.Info;

        findings.Add(new Finding
        {
            ModuleName = Name,
            Title = $"RAM {used:F1} GB used of {total:F1} GB ({percent:F0}%)",
            Details = $"{available:F1} GB available." + (severity == FindingSeverity.Warning
                ? " Memory is nearly full, which forces slow paging to disk and hurts performance."
                : " Memory headroom looks healthy."),
            Severity = severity,
            Recommendation = severity == FindingSeverity.Warning
                ? "Run the Optimize screen to free RAM, close unused apps/browser tabs, or add more memory."
                : null
        });

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
