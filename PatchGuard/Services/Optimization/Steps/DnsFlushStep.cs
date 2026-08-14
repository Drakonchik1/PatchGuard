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
        cancellationToken.ThrowIfCancellationRequested();
        var ipconfig = ResolveSystemExecutable(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "ipconfig.exe");
        if (ipconfig is null)
        {
            return new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Failed,
                Detail = "The canonical Windows ipconfig.exe path is unavailable."
            };
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

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new OptimizationStepResult
                {
                    StepName = Name,
                    Status = OptimizationStatus.Failed,
                    Detail = "Could not start ipconfig."
                };
            }

            using (process)
            {
                try
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                    var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);
                    await Task.WhenAll(outputTask, errorTask);

                    return new OptimizationStepResult
                    {
                        StepName = Name,
                        Status = process.ExitCode == 0 ? OptimizationStatus.Success : OptimizationStatus.Failed,
                        Detail = process.ExitCode == 0 ? "DNS resolver cache flushed." : $"ipconfig exited with code {process.ExitCode}."
                    };
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    throw;
                }
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
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

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cancellation cleanup.
        }
    }

    private static string? ResolveSystemExecutable(string directory, string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            var canonicalDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var directoryInfo = new DirectoryInfo(canonicalDirectory);
            directoryInfo.Refresh();
            if (!directoryInfo.Exists ||
                directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            var candidate = Path.GetFullPath(Path.Combine(canonicalDirectory, fileName));
            if (!string.Equals(
                    Path.GetDirectoryName(candidate),
                    canonicalDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var info = new FileInfo(candidate);
            info.Refresh();
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                ? candidate
                : null;
        }
        catch
        {
            return null;
        }
    }
}
