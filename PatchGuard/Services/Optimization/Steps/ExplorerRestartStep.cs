using System.Diagnostics;
using System.IO;
using PatchGuard.Models;

namespace PatchGuard.Services.Optimization.Steps;

/// <summary>
/// Restarts Windows Explorer (the desktop/taskbar shell). This frees memory the
/// shell has accumulated and clears UI glitches. It is optional because it
/// briefly hides the taskbar while Explorer relaunches.
/// </summary>
public sealed class ExplorerRestartStep : IOptimizationStep
{
    public string Name => "Restart Windows Explorer";
    public string Description => "Restarts the desktop shell to reclaim memory (taskbar blinks briefly).";
    public bool IsOptional => true;

    public async Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var killed = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var explorerPath = ResolveWindowsExecutable(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe");
            if (explorerPath is null)
            {
                return new OptimizationStepResult
                {
                    StepName = Name,
                    Status = OptimizationStatus.Failed,
                    Detail = "The canonical Windows explorer.exe path is unavailable."
                };
            }

            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    process.Kill();
                    killed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Give the shell a moment, then ensure it is running again.
            await Task.Delay(1500, cancellationToken);

            if (!IsExplorerRunning())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = false
                });
                if (process is null)
                {
                    return new OptimizationStepResult
                    {
                        StepName = Name,
                        Status = OptimizationStatus.Failed,
                        Detail = "Could not start the canonical Windows explorer.exe."
                    };
                }
            }

            return new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Success,
                Detail = killed > 0 ? "Explorer restarted." : "Explorer was not running; started it."
            };
        }
        catch (OperationCanceledException)
        {
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

    private static bool IsExplorerRunning()
    {
        var processes = Process.GetProcessesByName("explorer");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string? ResolveWindowsExecutable(string directory, string fileName)
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
