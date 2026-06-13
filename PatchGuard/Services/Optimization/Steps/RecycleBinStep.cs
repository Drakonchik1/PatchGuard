using PatchGuard.Models;
using PatchGuard.Services.Native;

namespace PatchGuard.Services.Optimization.Steps;

/// <summary>Empties the Recycle Bin exactly as Explorer would.</summary>
public sealed class RecycleBinStep : IOptimizationStep
{
    public string Name => "Empty Recycle Bin";
    public string Description => "Permanently removes items already in the Recycle Bin.";
    public bool IsOptional => false;

    public Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        long size = 0;
        try
        {
            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
            };
            if (NativeMethods.SHQueryRecycleBin(null, ref info) == 0)
            {
                size = info.i64Size;
            }
        }
        catch
        {
            // size stays 0; emptying may still proceed
        }

        try
        {
            const NativeMethods.RecycleFlags flags =
                NativeMethods.RecycleFlags.SHERB_NOCONFIRMATION |
                NativeMethods.RecycleFlags.SHERB_NOPROGRESSUI |
                NativeMethods.RecycleFlags.SHERB_NOSOUND;

            // S_OK (0) on success; -2147418113 / other when already empty.
            NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, flags);

            return Task.FromResult(new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Success,
                BytesFreed = size,
                Detail = size > 0 ? "Recycle Bin emptied." : "Recycle Bin was already empty."
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Failed,
                Detail = ex.Message
            });
        }
    }
}
