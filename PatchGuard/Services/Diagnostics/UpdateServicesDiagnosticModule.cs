using System.ServiceProcess;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class UpdateServicesDiagnosticModule : IDiagnosticModule
{
    private static readonly string[] ServiceNames = ["wuauserv", "BITS", "CryptSvc"];

    public string Name => "Update services";
    public string Description => "Checks status of wuauserv, BITS, and CryptSvc (read-only).";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        foreach (var serviceName in ServiceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var controller = new ServiceController(serviceName);
                var status = controller.Status;
                var severity = status == ServiceControllerStatus.Running
                    ? FindingSeverity.Info
                    : FindingSeverity.Warning;

                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = $"{serviceName}: {status}",
                    Details = status == ServiceControllerStatus.Running
                        ? "Service is running — good for Windows Update."
                        : "Service is not running. Updates may fail until it is started (requires admin — start manually via services.msc).",
                    Severity = severity,
                    Recommendation = status != ServiceControllerStatus.Running
                        ? "Open services.msc, find the service, and check its status. Starting it requires administrator rights."
                        : null
                });
            }
            catch (Exception ex)
            {
                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = $"{serviceName}: unavailable",
                    Details = ex.Message,
                    Severity = FindingSeverity.Warning
                });
            }
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
