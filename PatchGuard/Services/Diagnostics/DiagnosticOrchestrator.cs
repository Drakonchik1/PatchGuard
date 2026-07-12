using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class DiagnosticOrchestrator : IDiagnosticOrchestrator
{
    private readonly IReadOnlyList<IDiagnosticModule> _allModules;

    public DiagnosticOrchestrator(IEnumerable<IDiagnosticModule> modules)
    {
        _allModules = modules.ToList();
    }

    public IReadOnlyList<IDiagnosticModule> GetModulesForScenario(ScanScenario scenario)
    {
        return scenario switch
        {
            ScanScenario.FullSystemAudit => _allModules,
            ScanScenario.AfterWindowsUpdate => _allModules
                .Where(m => m is not (TemperatureDiagnosticModule or GpuInfoDiagnosticModule
                    or CpuLoadDiagnosticModule or MemoryLoadDiagnosticModule))
                .ToList(),
            ScanScenario.QuickHealthCheck => _allModules
                .Where(m => m is OsInfoDiagnosticModule or DiskSpaceDiagnosticModule or MemoryLoadDiagnosticModule)
                .ToList(),
            ScanScenario.GamePerformanceCheck => _allModules
                .Where(m => m is TemperatureDiagnosticModule or GpuInfoDiagnosticModule
                    or CpuLoadDiagnosticModule or MemoryLoadDiagnosticModule or DiskSpaceDiagnosticModule)
                .ToList(),
            _ => _allModules
        };
    }

    public async Task<IReadOnlyList<Finding>> RunScanAsync(
        ScanScenario scenario,
        IProgress<DiagnosticProgressItem> progress,
        CancellationToken cancellationToken = default)
    {
        var allFindings = new List<Finding>();

        foreach (var module in GetModulesForScenario(scenario))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = new DiagnosticProgressItem { ModuleName = module.Name };

            if (!module.IsImplemented)
            {
                item.Status = DiagnosticProgressStatus.Skipped;
                item.Message = "Planned for a future release.";
                progress.Report(item);
                continue;
            }

            item.Status = DiagnosticProgressStatus.Running;
            item.Message = module.Description;
            progress.Report(item);

            try
            {
                var findings = await module.RunAsync(cancellationToken);
                allFindings.AddRange(findings);
                item.Status = DiagnosticProgressStatus.Completed;
                item.Message = findings.Count == 0
                    ? "No issues reported."
                    : $"{findings.Count} item(s) recorded.";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Status = DiagnosticProgressStatus.Failed;
                item.Message = ex.Message;
            }

            progress.Report(item);
        }

        return allFindings;
    }
}
