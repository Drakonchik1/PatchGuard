using System.Diagnostics;
using System.IO;
using PatchGuard.Models;
using PatchGuard.Services.Platform;

namespace PatchGuard.Services.Performance;

/// <summary>
/// Captures real per-application frame timing using a bundled Intel PresentMon
/// console executable (MIT). PresentMon is launched with an argument list (never
/// a composed shell string) and only ever targets a numeric process id and a
/// PatchGuard-generated temp output path, so there is no command injection risk.
/// </summary>
public sealed class PresentMonFpsCaptureService : IFpsCaptureService
{
    private const string ExpectedPublisher = "Intel";

    private readonly IAdminElevationService _elevation;
    private readonly Lazy<(string? Path, string? Error)> _executable;

    public PresentMonFpsCaptureService(IAdminElevationService elevation)
    {
        _elevation = elevation;
        _executable = new Lazy<(string?, string?)>(LocatePresentMon);
    }

    public bool IsAvailable => _executable.Value.Path is not null;

    public string? UnavailableReason => _executable.Value.Error;

    public IReadOnlyList<GameProcessInfo> GetCandidateProcesses()
    {
        var self = Environment.ProcessId;
        var results = new List<GameProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == self || process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                var title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                results.Add(new GameProcessInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    WindowTitle = title
                });
            }
            catch
            {
                // Access denied for protected processes; skip.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results
            .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<FpsCaptureResult> CaptureAsync(
        GameProcessInfo target,
        int seconds,
        CancellationToken cancellationToken = default)
    {
        if (_executable.Value.Path is not { } exePath)
        {
            return FpsCaptureResult.Failed(
                target.ProcessName,
                _executable.Value.Error
                ?? "PresentMon was not found. Add PresentMon-x64.exe to the Tools\\PresentMon folder (see README.txt) to enable FPS capture.");
        }

        seconds = Math.Clamp(seconds, 3, 120);
        var csvPath = Path.Combine(Path.GetTempPath(), $"patchguard_fps_{Guid.NewGuid():N}.csv");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        };
        startInfo.ArgumentList.Add("--process_id");
        startInfo.ArgumentList.Add(target.ProcessId.ToString());
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(csvPath);
        startInfo.ArgumentList.Add("--timed");
        startInfo.ArgumentList.Add(seconds.ToString());
        startInfo.ArgumentList.Add("--terminate_after_timed");
        startInfo.ArgumentList.Add("--stop_existing_session");

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stderr = new System.Text.StringBuilder();
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!process.Start())
            {
                return FpsCaptureResult.Failed(target.ProcessName, "Failed to start PresentMon.");
            }

            process.BeginErrorReadLine();
            _ = process.StandardOutput.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(seconds + 15));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return FpsCaptureResult.Failed(target.ProcessName, "Capture timed out. Make sure the game is rendering frames.");
            }

            if (!File.Exists(csvPath))
            {
                var hint = _elevation.IsElevated
                    ? "No frame data was produced. The target may not present frames PresentMon can see (try a different process)."
                    : "No frame data was produced. Capturing another app usually requires running PatchGuard as administrator.";
                var detail = stderr.Length > 0 ? $" Details: {stderr.ToString().Trim()}" : string.Empty;
                return FpsCaptureResult.Failed(target.ProcessName, hint + detail);
            }

            return ParseCsv(csvPath, target.ProcessName);
        }
        catch (Exception ex)
        {
            return FpsCaptureResult.Failed(target.ProcessName, $"Capture error: {ex.Message}");
        }
        finally
        {
            TryDelete(csvPath);
        }
    }

    private static FpsCaptureResult ParseCsv(string csvPath, string processName)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
        {
            return FpsCaptureResult.Failed(processName, "Capture produced no frames. Try a longer capture while the game is active.");
        }

        var header = lines[0].Split(',');
        var frameTimeColumn = FindFrameTimeColumn(header);
        if (frameTimeColumn < 0)
        {
            return FpsCaptureResult.Failed(processName, "Could not find frame-time data in PresentMon output.");
        }

        var frameTimesMs = new List<double>(lines.Length);
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split(',');
            if (cells.Length <= frameTimeColumn)
            {
                continue;
            }

            if (double.TryParse(cells[frameTimeColumn], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ms) && ms > 0)
            {
                frameTimesMs.Add(ms);
            }
        }

        if (frameTimesMs.Count < 5)
        {
            return FpsCaptureResult.Failed(processName, "Too few frames captured. Run a longer capture while the game is rendering.");
        }

        var totalMs = frameTimesMs.Sum();
        var durationSec = totalMs / 1000.0;
        var avgFps = frameTimesMs.Count / durationSec;

        var sorted = frameTimesMs.OrderBy(x => x).ToList();
        var onePercentLow = 1000.0 / Percentile(sorted, 99.0);
        var pointOnePercentLow = 1000.0 / Percentile(sorted, 99.9);

        return new FpsCaptureResult
        {
            ProcessName = processName,
            Success = true,
            FrameCount = frameTimesMs.Count,
            DurationSeconds = durationSec,
            AverageFps = avgFps,
            OnePercentLowFps = onePercentLow,
            PointOnePercentLowFps = pointOnePercentLow,
            Message = $"{frameTimesMs.Count} frames over {durationSec:F1}s"
        };
    }

    private static int FindFrameTimeColumn(string[] header)
    {
        // PresentMon column naming has changed across versions; check the common ones.
        string[] candidates = ["msBetweenPresents", "FrameTime", "msBetweenDisplayChange", "MsBetweenPresents"];
        foreach (var candidate in candidates)
        {
            for (var i = 0; i < header.Length; i++)
            {
                if (header[i].Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Linear-interpolated percentile over an ascending-sorted list.</summary>
    private static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        if (sortedAscending.Count == 1)
        {
            return sortedAscending[0];
        }

        var rank = percentile / 100.0 * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        var weight = rank - lower;
        return sortedAscending[lower] * (1 - weight) + sortedAscending[upper] * weight;
    }

    private static (string? Path, string? Error) LocatePresentMon()
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Tools", "PresentMon");
            if (!Directory.Exists(dir))
            {
                return (null, null); // Not installed: caller shows the install hint.
            }

            var matches = Directory.EnumerateFiles(dir, "PresentMon*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (matches.Count == 0)
            {
                return (null, null);
            }

            // Fail closed: only run a binary with a valid Authenticode signature
            // from the expected publisher, so a planted exe in this user-writable
            // folder cannot be executed.
            var verified = matches
                .Where(f => AuthenticodeVerifier.IsTrusted(f, ExpectedPublisher))
                .ToList();

            if (verified.Count == 0)
            {
                return (null,
                    "A PresentMon executable was found but is not validly signed by Intel, so PatchGuard will not run it. " +
                    "Replace it with the official Intel PresentMon release (see Tools\\PresentMon\\README.txt).");
            }

            return (verified[0], null);
        }
        catch
        {
            return (null, null);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored
        }
    }
}
