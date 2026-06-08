using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public interface IDiagnosticModule
{
    string Name { get; }
    string Description { get; }
    bool IsImplemented { get; }
    Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default);
}
