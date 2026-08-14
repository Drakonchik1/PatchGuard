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

    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System
    };

    private static (long bytes, int count) CleanDirectory(string root, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var count = 0;
        var directories = new List<DirectoryInfo>();

        if (!TryGetSafeRoot(root, out var rootDirectory, out var rootPrefix))
        {
            return (0, 0);
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var directory = pending.Pop();
            if (!IsSafeContainedEntry(directory, rootPrefix, allowRoot: true))
            {
                continue;
            }

            FileSystemInfo[] entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos("*", SafeEnumeration).ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!IsSafeContainedEntry(entry, rootPrefix, allowRoot: false))
                {
                    continue;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    directories.Add(childDirectory);
                    pending.Push(childDirectory);
                    continue;
                }

                if (entry is FileInfo file && TryDeleteFile(file, rootPrefix, out var fileBytes))
                {
                    bytes += fileBytes;
                    count++;
                }
            }
        }

        foreach (var directory in directories.OrderByDescending(item => item.FullName.Length))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            TryDeleteEmptyDirectory(directory, rootPrefix);
        }

        return (bytes, count);
    }

    private static bool TryDeleteFile(FileInfo file, string rootPrefix, out long bytes)
    {
        bytes = 0;
        try
        {
            file.Refresh();
            if (!IsSafeContainedEntry(file, rootPrefix, allowRoot: false))
            {
                return false;
            }

            var length = file.Length;

            // Re-check the trust boundary immediately before deletion.
            file.Refresh();
            if (!IsSafeContainedEntry(file, rootPrefix, allowRoot: false))
            {
                return false;
            }

            file.Delete();
            bytes = length;
            return true;
        }
        catch
        {
            // Locked, changed, or inaccessible file; fail closed.
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(DirectoryInfo directory, string rootPrefix)
    {
        try
        {
            directory.Refresh();
            if (!IsSafeContainedEntry(directory, rootPrefix, allowRoot: false))
            {
                return;
            }

            if (directory.EnumerateFileSystemInfos("*", SafeEnumeration).Any())
            {
                return;
            }

            // Re-check the trust boundary immediately before deletion.
            directory.Refresh();
            if (IsSafeContainedEntry(directory, rootPrefix, allowRoot: false))
            {
                directory.Delete();
            }
        }
        catch
        {
            // Directory changed, is inaccessible, or is not empty; leave it alone.
        }
    }

    private static bool TryGetSafeRoot(
        string root,
        out DirectoryInfo rootDirectory,
        out string rootPrefix)
    {
        rootDirectory = null!;
        rootPrefix = string.Empty;
        try
        {
            var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            rootDirectory = new DirectoryInfo(rootFull);
            rootDirectory.Refresh();
            if (!rootDirectory.Exists ||
                rootDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            rootPrefix = rootFull + Path.DirectorySeparatorChar;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeContainedEntry(
        FileSystemInfo entry,
        string rootPrefix,
        bool allowRoot)
    {
        try
        {
            entry.Refresh();
            if (!entry.Exists || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(entry.FullName);
            if (HasReparsePointInAncestors(fullPath))
            {
                return false;
            }

            if (allowRoot &&
                string.Equals(
                    Path.TrimEndingDirectorySeparator(fullPath),
                    Path.TrimEndingDirectorySeparator(rootPrefix),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasReparsePointInAncestors(string fullPath)
    {
        var parent = Directory.GetParent(fullPath);
        while (parent is not null)
        {
            parent.Refresh();
            if (!parent.Exists || parent.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }

            parent = parent.Parent;
        }

        return false;
    }
}
