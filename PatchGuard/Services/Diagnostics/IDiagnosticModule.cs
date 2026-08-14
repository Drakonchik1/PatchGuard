using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public interface IDiagnosticModule
{
    string Name { get; }
    string Description { get; }
    bool IsImplemented { get; }

    /// <summary>
    /// True when invoking the module can perform synchronous WMI, event log,
    /// hardware, registry, service, or filesystem work before its task completes.
    /// </summary>
    bool RunsBlockingWork => true;

    Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default);
}
