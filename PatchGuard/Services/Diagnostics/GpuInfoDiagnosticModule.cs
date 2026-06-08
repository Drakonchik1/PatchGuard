using System.Management;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class GpuInfoDiagnosticModule : IDiagnosticModule
{
    public string Name => "GPU";
    public string Description => "Video adapter and driver.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                    var driver = obj["DriverVersion"]?.ToString() ?? "?";
                    var vramBytes = Convert.ToInt64(obj["AdapterRAM"] ?? 0);
                    var vramGb = vramBytes > 0 ? vramBytes / (1024.0 * 1024 * 1024) : 0;

                    findings.Add(new Finding
                    {
                        ModuleName = Name,
                        Title = name,
                        Details = $"Driver {driver}" + (vramGb > 0 ? $", VRAM ~{vramGb:F1} GB" : string.Empty),
                        Severity = FindingSeverity.Info
                    });
                }
            }
        }
        catch (Exception ex)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "GPU read failed",
                Details = ex.Message,
                Severity = FindingSeverity.Warning
            });
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "No GPU detected",
                Details = "WMI returned no video controller.",
                Severity = FindingSeverity.Warning
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
