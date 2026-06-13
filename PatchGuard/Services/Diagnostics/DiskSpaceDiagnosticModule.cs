using System.IO;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class DiskSpaceDiagnosticModule : IDiagnosticModule
{
    private const long WarningThresholdBytes = 10L * 1024 * 1024 * 1024;

    public string Name => "Disk space";
    public string Description => "Checks free space on system drive C:.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        try
        {
            var drive = new DriveInfo("C");
            if (!drive.IsReady)
            {
                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = "Drive C: is not ready",
                    Details = "Could not read disk information.",
                    Severity = FindingSeverity.Warning
                });
                return Task.FromResult<IReadOnlyList<Finding>>(findings);
            }

            var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
            var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
            var severity = drive.AvailableFreeSpace < WarningThresholdBytes
                ? FindingSeverity.Warning
                : FindingSeverity.Info;

            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = $"C: has {freeGb:F1} GB free of {totalGb:F0} GB",
                Details = severity == FindingSeverity.Warning
                    ? "Windows updates and rollback files need free space. Less than 10 GB can cause failed updates."
                    : "Free space looks adequate for routine updates.",
                Severity = severity,
                Recommendation = severity == FindingSeverity.Warning
                    ? "Free up space via Settings → System → Storage before installing large updates."
                    : null
            });
        }
        catch (Exception ex)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Disk check failed",
                Details = ex.Message,
                Severity = FindingSeverity.Warning
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
