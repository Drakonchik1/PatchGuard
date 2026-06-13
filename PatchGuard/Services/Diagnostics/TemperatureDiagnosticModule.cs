using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Services.Diagnostics;

public sealed class TemperatureDiagnosticModule : IDiagnosticModule
{
    private const double WarnTempC = 85;
    private const double CriticalTempC = 95;

    private readonly IHardwareMonitorService _hardware;

    public TemperatureDiagnosticModule(IHardwareMonitorService hardware)
    {
        _hardware = hardware;
    }

    public string Name => "Temperatures";
    public string Description => "Reads CPU and GPU temperatures from hardware sensors.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var snapshot = _hardware.Capture();

        if (snapshot.MonitorUnavailable)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Temperature sensors unavailable",
                Details = snapshot.StatusMessage ?? "Hardware sensor library could not be initialised.",
                Severity = FindingSeverity.Info
            });
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        AddTemperatureFinding(findings, "CPU", snapshot.CpuName, snapshot.CpuTemperatureC);
        AddTemperatureFinding(findings, "GPU", snapshot.GpuName, snapshot.GpuTemperatureC);

        if (snapshot is { CpuTemperatureC: null, GpuTemperatureC: null })
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "No temperature readings available",
                Details = snapshot.SensorsLimited
                    ? "Temperature sensors usually require administrator rights. Use 'Run as admin' on the Monitor screen for full readings."
                    : "No temperature sensors were exposed by this hardware.",
                Severity = FindingSeverity.Info,
                Recommendation = snapshot.SensorsLimited ? "Relaunch PatchGuard as administrator to read temperatures." : null
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private void AddTemperatureFinding(List<Finding> findings, string label, string deviceName, double? tempC)
    {
        if (tempC is not { } temp)
        {
            return;
        }

        var severity = temp >= CriticalTempC
            ? FindingSeverity.Critical
            : temp >= WarnTempC
                ? FindingSeverity.Warning
                : FindingSeverity.Info;

        findings.Add(new Finding
        {
            ModuleName = Name,
            Title = $"{label} temperature {temp:F0} °C",
            Details = $"{deviceName} is at {temp:F0} °C." + severity switch
            {
                FindingSeverity.Critical => " This is very hot and may cause thermal throttling or shutdowns.",
                FindingSeverity.Warning => " This is warm under load; check airflow and dust if it stays high at idle.",
                _ => " This is within a normal range."
            },
            Severity = severity,
            Recommendation = severity >= FindingSeverity.Warning
                ? "Improve case airflow, clean dust from fans/heatsinks, and verify the cooler is seated. Avoid blocking vents."
                : null
        });
    }
}
