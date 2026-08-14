using System.Diagnostics;
using System.IO;
using System.Reflection;
using PatchGuard.Services.Optimization.Steps;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;

namespace PatchGuard.Tests;

public sealed class TempFilesCleanStepTests
{
    [Fact]
    public void CleanDirectory_DoesNotTraverseJunctionRoot()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"patchguard-junction-{Guid.NewGuid():N}");
        var target = Path.Combine(parent, "outside");
        var junction = Path.Combine(parent, "cleanup-root");
        var marker = Path.Combine(target, "must-survive.txt");
        Directory.CreateDirectory(target);
        File.WriteAllText(marker, "keep");

        try
        {
            var command = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/c", "mklink", "/J", junction, target }
            });
            Assert.NotNull(process);
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    $"Junction creation was denied: {process.StandardError.ReadToEnd()}");
            }

            var cleanDirectory = typeof(TempFilesCleanStep).GetMethod(
                "CleanDirectory",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(cleanDirectory);

            cleanDirectory.Invoke(null, [junction, CancellationToken.None]);

            Assert.True(File.Exists(marker), "Cleanup followed a junction outside its trusted root.");
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }

            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }
}

public sealed class ProcessCancellationTests
{
    [Fact]
    public async Task DnsFlush_RethrowsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DnsFlushStep().RunAsync(cancellation.Token));
    }

    [Fact]
    public async Task ExplorerRestart_RethrowsCancellationBeforeChangingProcesses()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ExplorerRestartStep().RunAsync(cancellation.Token));
    }

    [Fact]
    public async Task PresentMonCapture_RethrowsCancellationEvenWhenUnavailable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new PresentMonFpsCaptureService(new StubElevationService());
        var target = new PatchGuard.Models.GameProcessInfo
        {
            ProcessId = Environment.ProcessId,
            ProcessName = "test"
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CaptureAsync(target, 3, cancellation.Token));
    }

    [Fact]
    public async Task PresentMonCapture_ReverifiesExecutableImmediatelyBeforeLaunch()
    {
        var fakeExecutable = Path.Combine(
            Path.GetTempPath(),
            $"PresentMon-{Guid.NewGuid():N}.exe");
        await File.WriteAllTextAsync(fakeExecutable, "not a signed executable");
        try
        {
            var service = new PresentMonFpsCaptureService(new StubElevationService());
            var executableField = typeof(PresentMonFpsCaptureService).GetField(
                "_executable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(executableField);
            executableField.SetValue(
                service,
                new Lazy<(string? Path, string? Error)>(() => (fakeExecutable, null)));
            var target = new PatchGuard.Models.GameProcessInfo
            {
                ProcessId = Environment.ProcessId,
                ProcessName = "test"
            };

            var result = await service.CaptureAsync(target, 3);

            Assert.False(result.Success);
            Assert.Contains(
                "signature verification failed immediately before launch",
                result.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(fakeExecutable);
        }
    }

    private sealed class StubElevationService : IAdminElevationService
    {
        public bool IsElevated => false;

        public bool RestartElevated() => false;
    }
}
