using System.Diagnostics;
using System.IO;
using PatchGuard.Models;

namespace PatchGuard.Services.Optimization.Steps;

/// <summary>
/// Flushes the DNS resolver cache (ipconfig /flushdns). This clears a transient
/// cache only; it does not change any network configuration.
/// </summary>
public sealed class DnsFlushStep : IOptimizationStep
{
    public string Name => "Flush DNS cache";
    public string Description => "Clears the DNS resolver cache to fix stale name lookups.";
    public bool IsOptional => false;

    public async Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var ipconfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "ipconfig.exe");

        if (!File.Exists(ipconfig))
        {
            ipconfig = "ipconfig.exe";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ipconfig,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("/flushdns");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new OptimizationStepResult
                {
                    StepName = Name,
                    Status = OptimizationStatus.Failed,
                    Detail = "Could not start ipconfig."
                };
            }

            _ = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new OptimizationStepResult
            {
                StepName = Name,
                Status = process.ExitCode == 0 ? OptimizationStatus.Success : OptimizationStatus.Failed,
                Detail = process.ExitCode == 0 ? "DNS resolver cache flushed." : $"ipconfig exited with code {process.ExitCode}."
            };
        }
        catch (Exception ex)
        {
            return new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Failed,
                Detail = ex.Message
            };
        }
    }
}
