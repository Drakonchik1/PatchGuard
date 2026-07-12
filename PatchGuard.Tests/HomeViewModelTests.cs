using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task RefreshBuildsDashboardSummaryFromLatestScanAndSnapshot()
    {
        var history = new FakeHistoryService(
        [
            new ScanHistoryEntry
            {
                ScannedAt = new DateTime(2026, 7, 12, 10, 30, 0),
                ScenarioTitle = "Quick health check",
                HealthScore = 72,
                WarningCount = 3,
                FindingCount = 4,
                TrendLabel = "Down 5"
            }
        ]);
        var hardware = new FakeHardwareMonitorService(new HardwareSnapshot
        {
            CpuLoadPercent = 34,
            RamUsedGb = 8,
            RamTotalGb = 16,
            RamLoadPercent = 50
        });

        var viewModel = CreateViewModel(history, hardware);
        await viewModel.RefreshDashboardAsync();

        Assert.Equal("72 / 100", viewModel.LatestHealthText);
        Assert.Equal(
            "Run a quick health scan to re-check 3 warnings from your latest scan.",
            viewModel.RecommendedAction);
        Assert.True(viewModel.HasHardwareSnapshot);
        Assert.Equal("34%", viewModel.CpuLoadText);
        Assert.Equal("8.0 / 16.0 GB", viewModel.MemoryText);
    }

    [Fact]
    public async Task RefreshUsesHonestEmptyAndUnavailableStates()
    {
        var viewModel = CreateViewModel(
            new FakeHistoryService([]),
            new FakeHardwareMonitorService(new HardwareSnapshot { MonitorUnavailable = true }));

        await viewModel.RefreshDashboardAsync();

        Assert.False(viewModel.HasScanHistory);
        Assert.Equal("No scan data yet", viewModel.LatestHealthText);
        Assert.False(viewModel.HasHardwareSnapshot);
        Assert.Equal("Live snapshot unavailable", viewModel.HardwareStatusText);
    }

    [Fact]
    public async Task RefreshReportsHistoryFailureWithoutCrashingDashboard()
    {
        var viewModel = CreateViewModel(
            new ThrowingHistoryService(),
            new FakeHardwareMonitorService(new HardwareSnapshot()));

        await viewModel.RefreshDashboardAsync();

        Assert.Equal(
            "Scan history is temporarily unavailable.",
            viewModel.DashboardErrorMessage);
    }

    [Fact]
    public async Task RefreshCapturesHardwareOffCallingThread()
    {
        var hardware = new ThreadRecordingHardwareMonitorService();
        var viewModel = CreateViewModel(new FakeHistoryService([]), hardware);
        var callingThreadId = Environment.CurrentManagedThreadId;

        await viewModel.RefreshDashboardAsync();

        Assert.NotEqual(callingThreadId, hardware.CaptureThreadId);
    }

    [Fact]
    public async Task NavigatingAwayPreventsInFlightSnapshotFromUpdatingDashboard()
    {
        var hardware = new BlockingHardwareMonitorService(new HardwareSnapshot
        {
            CpuLoadPercent = 87,
            RamUsedGb = 12,
            RamTotalGb = 16
        });
        var viewModel = CreateViewModel(new FakeHistoryService([]), hardware);
        var refreshTask = Task.Run(() => viewModel.RefreshDashboardAsync());

        await hardware.CaptureStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var navigationLeave = Assert.IsAssignableFrom<INavigationLeave>(viewModel);
            navigationLeave.OnNavigatedFrom();
        }
        finally
        {
            hardware.ReleaseCapture();
        }

        await refreshTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Unavailable", viewModel.CpuLoadText);
        Assert.False(viewModel.HasHardwareSnapshot);
    }

    [Theory]
    [InlineData("Monitor", typeof(MonitorViewModel))]
    [InlineData("GamePerformance", typeof(FpsViewModel))]
    [InlineData("Optimize", typeof(OptimizeViewModel))]
    public void QuickAccessCommandsNavigateToExactDestination(string command, Type expectedType)
    {
        var navigation = new RecordingNavigationService();
        var viewModel = CreateViewModel(
            new FakeHistoryService([]),
            new FakeHardwareMonitorService(new HardwareSnapshot()),
            navigation);

        switch (command)
        {
            case "Monitor":
                viewModel.OpenMonitorCommand.Execute(null);
                break;
            case "GamePerformance":
                viewModel.OpenGamePerformanceCommand.Execute(null);
                break;
            case "Optimize":
                viewModel.OpenOptimizeCommand.Execute(null);
                break;
        }

        Assert.Equal(expectedType, navigation.DestinationType);
    }

    private static HomeViewModel CreateViewModel(
        IScanHistoryService history,
        IHardwareMonitorService hardware,
        INavigationService? navigation = null) =>
        new(
            navigation ?? new RecordingNavigationService(),
            new ScanSessionState(),
            history,
            hardware);

    private sealed class FakeHistoryService(IReadOnlyList<ScanHistoryEntry> scans) : IScanHistoryService
    {
        public Task SaveScanAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
            int take = 6,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanHistoryEntry>>(scans);
    }

    private sealed class FakeHardwareMonitorService(HardwareSnapshot snapshot) : IHardwareMonitorService
    {
        public HardwareSnapshot Capture() => snapshot;
        public void Dispose() { }
    }

    private sealed class ThreadRecordingHardwareMonitorService : IHardwareMonitorService
    {
        public int CaptureThreadId { get; private set; }

        public HardwareSnapshot Capture()
        {
            CaptureThreadId = Environment.CurrentManagedThreadId;
            return new HardwareSnapshot();
        }

        public void Dispose() { }
    }

    private sealed class BlockingHardwareMonitorService(HardwareSnapshot snapshot) : IHardwareMonitorService
    {
        private readonly ManualResetEventSlim _release = new();

        public TaskCompletionSource CaptureStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HardwareSnapshot Capture()
        {
            CaptureStarted.TrySetResult();
            _release.Wait(TimeSpan.FromSeconds(5));
            return snapshot;
        }

        public void ReleaseCapture() => _release.Set();
        public void Dispose() => _release.Dispose();
    }

    private sealed class ThrowingHistoryService : IScanHistoryService
    {
        public Task SaveScanAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
            int take = 6,
            CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<ScanHistoryEntry>>(new InvalidOperationException("Database unavailable"));
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public Type? DestinationType { get; private set; }
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : class =>
            DestinationType = typeof(TViewModel);
        public void NavigateHome() => DestinationType = typeof(HomeViewModel);
        public void GoBack() { }
    }
}
