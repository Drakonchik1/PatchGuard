using System.Diagnostics;
using PatchGuard.Models;
using PatchGuard.Services.Native;

namespace PatchGuard.Services.Optimization.Steps;

/// <summary>
/// Asks Windows to trim the working set of every accessible process. This pages
/// out memory that is not actively in use; the OS transparently re-loads pages
/// on demand, so nothing is lost and no setting is changed. Freed memory is
/// measured from the system-wide available-memory delta.
/// </summary>
public sealed class WorkingSetTrimStep : IOptimizationStep
{
    public string Name => "Free up RAM (working sets)";
    public string Description => "Trims unused memory pages from running processes. Safe and automatic.";
    public bool IsOptional => false;

    public Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var availableBefore = GetAvailableBytes();
        var trimmed = 0;

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (NativeMethods.EmptyWorkingSet(process.Handle))
                {
                    trimmed++;
                }
            }
            catch
            {
                // Protected/system processes deny access; skip them quietly.
            }
            finally
            {
                process.Dispose();
            }
        }

        var availableAfter = GetAvailableBytes();
        var freed = availableAfter > availableBefore ? (long)(availableAfter - availableBefore) : 0;

        return Task.FromResult(new OptimizationStepResult
        {
            StepName = Name,
            Status = OptimizationStatus.Success,
            BytesFreed = freed,
            Detail = $"Trimmed {trimmed} process(es)."
        });
    }

    private static ulong GetAvailableBytes()
    {
        try
        {
            var status = new NativeMethods.MEMORYSTATUSEX();
            return NativeMethods.GlobalMemoryStatusEx(status) ? status.ullAvailPhys : 0;
        }
        catch
        {
            return 0;
        }
    }
}
