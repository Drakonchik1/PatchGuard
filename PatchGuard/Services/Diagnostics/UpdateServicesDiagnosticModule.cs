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
                findings.Add(UpdateServiceHealthEvaluator.CreateFinding(
                    serviceName,
                    status,
                    controller.StartType));
            }
            catch (Exception ex)
            {
                findings.Add(FindingFactory.Unavailable(
                    Name,
                    $"{serviceName}: unavailable",
                    $"Service status query for {serviceName} failed with {ex.GetType().Name}."));
            }
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }
}
