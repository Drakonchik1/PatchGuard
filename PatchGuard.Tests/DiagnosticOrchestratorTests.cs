using PatchGuard.Models;
using PatchGuard.Services.Diagnostics;

namespace PatchGuard.Tests;

public sealed class DiagnosticOrchestratorTests
{
    [Fact]
    public async Task ModuleCancellationIsRethrown()
    {
        var orchestrator = new DiagnosticOrchestrator([new CancellingModule()]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.RunScanAsync(
                ScanScenario.FullSystemAudit,
                new Progress<DiagnosticProgressItem>(),
                CancellationToken.None));
    }

    private sealed class CancellingModule : IDiagnosticModule
    {
        public string Name => "Cancellation";
        public string Description => "Cancels";
        public bool IsImplemented => true;

        public Task<IReadOnlyList<Finding>> RunAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<Finding>>(
                new OperationCanceledException(cancellationToken));
    }
}
