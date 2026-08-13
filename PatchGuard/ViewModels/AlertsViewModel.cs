using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services.Alerts;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public sealed class AlertDisplayItem
{
    public required Alert Alert { get; init; }
    public required bool IsActive { get; init; }

    public string StatusLabel => IsActive ? "Active" : "Resolved";
    public string SeverityLabel => Alert.Severity.ToString();
    public string TimestampText => Alert.Timestamp.ToString("g");
    public string Message => Alert.Message;
    public string Metric => Alert.Metric;
    public string RecommendedAction => Alert.RecommendedAction;
    public AlertSeverity Severity => Alert.Severity;
    public string ValueText => $"{Alert.Value:F0} / threshold {Alert.Threshold:F0}";
}

public partial class AlertsViewModel : ObservableObject, INavigationAware
{
    private readonly IHardwareMonitorService _hardware;
    private readonly ISensorHistoryService _sensorHistory;
    private readonly IAlertRuleEngine _alertRules;
    private readonly INavigationService _navigation;

    public AlertsViewModel(
        IHardwareMonitorService hardware,
        ISensorHistoryService sensorHistory,
        IAlertRuleEngine alertRules,
        INavigationService navigation)
    {
        _hardware = hardware;
        _sensorHistory = sensorHistory;
        _alertRules = alertRules;
        _navigation = navigation;
    }

    public ObservableCollection<AlertDisplayItem> ActiveAlerts { get; } = [];
    public ObservableCollection<AlertDisplayItem> ResolvedAlerts { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasActiveAlerts;

    [ObservableProperty]
    private bool _hasResolvedAlerts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasAnyAlerts;

    public bool ShowEmptyState => !HasAnyAlerts;

    [ObservableProperty]
    private string _summaryText = "No threshold alerts right now.";

    [ObservableProperty]
    private string? _errorMessage;

    public void OnNavigatedTo() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            HardwareSnapshot live;
            try
            {
                live = await Task.Run(_hardware.Capture);
            }
            catch
            {
                live = new HardwareSnapshot
                {
                    MonitorUnavailable = true,
                    StatusMessage = "Live snapshot unavailable"
                };
            }

            IReadOnlyList<Data.Entities.SensorSnapshotRecord> history;
            try
            {
                history = await _sensorHistory.GetRecentAsync(40);
            }
            catch
            {
                history = [];
                ErrorMessage = "Sensor history is temporarily unavailable. Showing live alerts only.";
            }

            var active = _alertRules.Evaluate(live)
                .OrderByDescending(a => a.Severity)
                .ThenByDescending(a => a.Timestamp)
                .ToList();
            var activeIds = active.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var resolved = new List<AlertDisplayItem>();
            var seenResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in history)
            {
                foreach (var alert in _alertRules.Evaluate(snapshot)
                             .OrderByDescending(a => a.Severity))
                {
                    if (activeIds.Contains(alert.Id) || !seenResolved.Add(alert.Id))
                    {
                        continue;
                    }

                    resolved.Add(new AlertDisplayItem { Alert = alert, IsActive = false });
                }
            }

            ActiveAlerts.Clear();
            foreach (var alert in active)
            {
                ActiveAlerts.Add(new AlertDisplayItem { Alert = alert, IsActive = true });
            }

            ResolvedAlerts.Clear();
            foreach (var item in resolved.Take(20))
            {
                ResolvedAlerts.Add(item);
            }

            HasActiveAlerts = ActiveAlerts.Count > 0;
            HasResolvedAlerts = ResolvedAlerts.Count > 0;
            HasAnyAlerts = HasActiveAlerts || HasResolvedAlerts;

            SummaryText = HasActiveAlerts
                ? $"{ActiveAlerts.Count} active · highest {active.Max(a => a.Severity)}"
                : HasResolvedAlerts
                    ? "No active alerts. Recent threshold breaches are listed below."
                    : "No threshold alerts right now. Open Live Monitor to keep sensor history fresh.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenMonitor() => _navigation.NavigateTo<MonitorViewModel>();
}
