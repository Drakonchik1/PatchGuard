using System.Management;
using System.Runtime.Versioning;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

[SupportedOSPlatform("windows")]
public sealed class GpuInfoDiagnosticModule : IDiagnosticModule
{
    public string Name => "Graphics card";
    public string Description => "Reads GPU model, driver version, and video memory.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (obj)
                {
                    var name = obj["Name"] as string ?? "Unknown GPU";
                    var driver = obj["DriverVersion"] as string;
                    var driverDate = ParseWmiDate(obj["DriverDate"] as string);
                    var vramText = FormatVram(obj["AdapterRAM"]);

                    var details = $"Driver {driver ?? "unknown"}";
                    if (driverDate is { } date)
                    {
                        details += $" ({date:yyyy-MM-dd})";
                    }
                    if (vramText is not null)
                    {
                        details += $", {vramText} reported VRAM";
                    }

                    findings.Add(new Finding
                    {
                        ModuleName = Name,
                        Title = name,
                        Details = details,
                        Severity = FindingSeverity.Info,
                        Recommendation = "Keep GPU drivers current via NVIDIA App / AMD Adrenalin / Intel Arc Control for the best game performance and stability."
                    });
                }
            }

            if (findings.Count == 0)
            {
                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = "No display adapter detected",
                    Details = "WMI did not report any video controllers.",
                    Severity = FindingSeverity.Info
                });
            }
        }
        catch (Exception ex)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "GPU information unavailable",
                Details = ex.Message,
                Severity = FindingSeverity.Warning
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static string? FormatVram(object? adapterRam)
    {
        try
        {
            // AdapterRAM is a UInt32 and saturates at ~4 GB, so treat it as a hint only.
            var bytes = Convert.ToInt64(adapterRam);
            if (bytes <= 0)
            {
                return null;
            }

            var gb = bytes / (1024.0 * 1024 * 1024);
            return gb >= 1 ? $"{gb:F1} GB" : $"{bytes / (1024.0 * 1024):F0} MB";
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? ParseWmiDate(string? wmiDate)
    {
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 8)
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(wmiDate);
        }
        catch
        {
            return null;
        }
    }
}
