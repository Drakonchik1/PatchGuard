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
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                    killed++;
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

            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                var explorer = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = File.Exists(explorer) ? explorer : "explorer.exe",
                    UseShellExecute = true
                });
            }

            return new OptimizationStepResult
            {
                StepName = Name,
                Status = OptimizationStatus.Success,
                Detail = killed > 0 ? "Explorer restarted." : "Explorer was not running; started it."
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
