using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class PlaceholderDiagnosticModule : IDiagnosticModule
{
    public PlaceholderDiagnosticModule(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
    public bool IsImplemented => false;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Finding>>([]);
}
