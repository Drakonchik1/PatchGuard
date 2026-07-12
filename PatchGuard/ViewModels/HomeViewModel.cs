using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class HomeViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IScanHistoryService _history;
    private readonly IHardwareMonitorService _hardware;
    private CancellationTokenSource? _refreshCts;
    private int _refreshGeneration;

    public HomeViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IScanHistoryService history,
        IHardwareMonitorService hardware)
    {
        _navigation = navigation;
        _session = session;
        _history = history;
        _hardware = hardware;

        Scenarios = new ObservableCollection<ScenarioOption>(ScanScenarioWorkflow.CreateOptions());
    }

    public ObservableCollection<ScenarioOption> Scenarios { get; }
    public ObservableCollection<ScanHistoryEntry> RecentScans { get; } = [];

    [ObservableProperty]
    private string _machineLabel = Environment.MachineName;

    [ObservableProperty]
    private bool _hasScanHistory;

    [ObservableProperty]
    private string _latestHealthText = "No scan data yet";

    [ObservableProperty]
    private string _latestScanSummary = "Run a diagnostic scan to establish a health baseline.";

    [ObservableProperty]
    private string _recommendedAction = "Run a quick health check to get personalized recommendations.";

    [ObservableProperty]
    private bool _hasHardwareSnapshot;

    [ObservableProperty]
    private string _hardwareStatusText = "Live snapshot unavailable";

    [ObservableProperty]
    private string _cpuLoadText = "Unavailable";

    [ObservableProperty]
    private string _memoryText = "Unavailable";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDashboardError))]
    private string? _dashboardErrorMessage;

    public bool HasDashboardError => !string.IsNullOrWhiteSpace(DashboardErrorMessage);

    public void OnNavigatedTo()
    {
        CancelRefresh();
        _refreshCts = new CancellationTokenSource();
        _ = RefreshDashboardAsync(_refreshCts.Token);
    }

    public void OnNavigatedFrom() => CancelRefresh();

    /// <summary>Refreshes the dashboard's history and one-time hardware summary.</summary>
    public async Task RefreshDashboardAsync(CancellationToken cancellationToken = default)
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        IReadOnlyList<ScanHistoryEntry> items = [];
        string? dashboardError = null;
        try
        {
            items = await _history.GetRecentScansAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            dashboardError = "Scan history is temporarily unavailable.";
        }

        HardwareSnapshot snapshot;
        try
        {
            snapshot = await Task.Run(_hardware.Capture, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            snapshot = new HardwareSnapshot
            {
                MonitorUnavailable = true,
                StatusMessage = "Live snapshot unavailable"
            };
        }

        if (cancellationToken.IsCancellationRequested
            || generation != Volatile.Read(ref _refreshGeneration))
        {
            return;
        }

        ApplyDashboardState(items, snapshot, dashboardError);
    }

    private void ApplyDashboardState(
        IReadOnlyList<ScanHistoryEntry> items,
        HardwareSnapshot snapshot,
        string? dashboardError)
    {
        RecentScans.Clear();
        DashboardErrorMessage = dashboardError;
        foreach (var item in items)
        {
            RecentScans.Add(item);
        }

        HasScanHistory = RecentScans.Count > 0;
        var latest = RecentScans.OrderByDescending(scan => scan.ScannedAt).FirstOrDefault();
        if (latest is null)
        {
            LatestHealthText = "No scan data yet";
            LatestScanSummary = "Run a diagnostic scan to establish a health baseline.";
            RecommendedAction = "Run a quick health check to get personalized recommendations.";
        }
        else
        {
            LatestHealthText = $"{latest.HealthScore} / 100";
            LatestScanSummary = $"{latest.ScenarioTitle} · {latest.ScannedAt:g} · {latest.TrendLabel}";
            RecommendedAction = latest.WarningCount > 0
                ? $"Run a quick health scan to re-check {latest.WarningCount} warning{(latest.WarningCount == 1 ? string.Empty : "s")} from your latest scan."
                : "Your latest scan has no warnings. Keep monitoring system health.";
        }

        HasHardwareSnapshot = !snapshot.MonitorUnavailable
            && (snapshot.CpuLoadPercent.HasValue || snapshot.RamUsedGb.HasValue);
        HardwareStatusText = HasHardwareSnapshot
            ? snapshot.SensorsLimited
                ? "Live summary available; some sensors require administrator rights."
                : $"Captured {snapshot.CapturedAt:t}"
            : "Live snapshot unavailable";
        CpuLoadText = snapshot.CpuLoadPercent is { } cpuLoad ? $"{cpuLoad:F0}%" : "Unavailable";
        MemoryText = snapshot is { RamUsedGb: { } used, RamTotalGb: { } total }
            ? $"{used:F1} / {total:F1} GB"
            : "Unavailable";
    }

    [RelayCommand]
    private void StartScan(ScenarioOption? option) =>
        ScanScenarioWorkflow.TryStart(option, _session, _navigation);

    [RelayCommand]
    private void OpenMonitor() => _navigation.NavigateTo<MonitorViewModel>();

    [RelayCommand]
    private void OpenGamePerformance() => _navigation.NavigateTo<FpsViewModel>();

    [RelayCommand]
    private void OpenOptimize() => _navigation.NavigateTo<OptimizeViewModel>();

    private void CancelRefresh()
    {
        Interlocked.Increment(ref _refreshGeneration);
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }
}
