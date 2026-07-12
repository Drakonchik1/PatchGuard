using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Services.Diagnostics;

public sealed class CpuLoadDiagnosticModule : IDiagnosticModule
{
    private const double HighLoadPercent = 90;

    private readonly IHardwareMonitorService _hardware;

    public CpuLoadDiagnosticModule(IHardwareMonitorService hardware)
    {
        _hardware = hardware;
    }

    public string Name => "CPU load";
    public string Description => "Reads current CPU utilisation and clock speed.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var snapshot = _hardware.Capture();

        if (snapshot.CpuLoadPercent is not { } load)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "CPU load unavailable",
                Details = "Could not read CPU utilisation from sensors.",
                Severity = FindingSeverity.Info,
                Evidence = "CPU utilisation sensor returned no reading.",
                ActionState = FindingActionState.Unavailable,
                AdminRequirement = FindingAdminRequirement.Unknown,
                Risk = FindingRisk.Unknown,
                VerificationStatus = FindingVerificationStatus.NotVerified
            });
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var clockText = snapshot.CpuClockMhz is { } mhz ? $" at {mhz:F0} MHz" : string.Empty;
        var severity = load >= HighLoadPercent ? FindingSeverity.Warning : FindingSeverity.Info;

        findings.Add(new Finding
        {
            ModuleName = Name,
            Title = $"CPU at {load:F0}% load",
            Details = $"{snapshot.CpuName} is running at {load:F0}% utilisation{clockText}." +
                      (severity == FindingSeverity.Warning
                          ? " Sustained high load can indicate a runaway background process."
                          : " Utilisation looks normal."),
            Severity = severity,
            Evidence = snapshot.CpuClockMhz is { } clock
                ? $"CPU load sensor reported {load:F0}% at {clock:F0} MHz."
                : $"CPU load sensor reported {load:F0}%; clock speed was unavailable.",
            Recommendation = severity == FindingSeverity.Warning
                ? "Open Task Manager → Details, sort by CPU, and close or update any unexpected high-usage process."
                : null,
            ActionState = severity == FindingSeverity.Warning
                ? FindingActionState.Recommended
                : FindingActionState.None,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = severity == FindingSeverity.Warning ? FindingRisk.Low : FindingRisk.NotApplicable,
            VerificationStatus = severity == FindingSeverity.Warning
                ? FindingVerificationStatus.NotVerified
                : FindingVerificationStatus.NotRequired
        });

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
