using System.IO;
using PatchGuard.Models;

namespace PatchGuard.Services.Optimization.Steps;

/// <summary>
/// Deletes leftover files from well-known temporary folders only. Files that are
/// in use are skipped (their locks throw and are ignored). No other location is
/// ever touched, so this cannot remove user documents or change settings.
/// </summary>
public sealed class TempFilesCleanStep : IOptimizationStep
{
    public string Name => "Clear temporary files";
    public string Description => "Removes leftover files from Windows and user temp/cache folders.";
    public bool IsOptional => false;

    public Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
    {
        long freed = 0;
        var deleted = 0;

        foreach (var root in GetTempRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            (var bytes, var count) = CleanDirectory(root, cancellationToken);
            freed += bytes;
            deleted += count;
        }

        return Task.FromResult(new OptimizationStepResult
        {
            StepName = Name,
            Status = OptimizationStatus.Success,
            BytesFreed = freed,
            Detail = $"Removed {deleted} file(s)."
        });
    }

    private static IEnumerable<string> GetTempRoots()
    {
        yield return Path.GetTempPath();

        var windir = Environment.GetEnvironmentVariable("WINDIR");
        if (!string.IsNullOrWhiteSpace(windir))
        {
            yield return Path.Combine(windir, "Temp");
        }

        yield return Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
    }

    // Skip symlinks/junctions (and hidden/system OS files) so cleanup can never
    // follow a reparse point out of the temp root and delete unrelated data.
    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    private static (long bytes, int count) CleanDirectory(string root, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var count = 0;

        // Canonical root used as a containment boundary for every delete.
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SafeEnumeration);
        }
        catch
        {
            return (0, 0);
        }

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!IsContained(file, rootFull))
                {
                    continue;
                }

                var info = new FileInfo(file);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var size = info.Length;
                info.Delete();
                bytes += size;
                count++;
            }
            catch
            {
                // Locked or in-use file; leave it alone.
            }
        }

        TryRemoveEmptyDirectories(root, rootFull, cancellationToken);
        return (bytes, count);
    }

    private static void TryRemoveEmptyDirectories(string root, string rootFull, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SafeEnumeration)
                         .OrderByDescending(d => d.Length))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    if (IsContained(dir, rootFull) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool IsContained(string path, string rootFull)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
