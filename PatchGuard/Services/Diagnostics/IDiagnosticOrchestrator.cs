using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public interface IDiagnosticOrchestrator
{
    IReadOnlyList<IDiagnosticModule> GetModulesForScenario(ScanScenario scenario);
    Task<IReadOnlyList<Finding>> RunScanAsync(
        ScanScenario scenario,
        IProgress<DiagnosticProgressItem> progress,
        CancellationToken cancellationToken = default);
}
