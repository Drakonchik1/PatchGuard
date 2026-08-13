using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services.Alerts;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.History;
using PatchGuard.Services.Ml;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public partial class MonitorViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AnomalyInterval = TimeSpan.FromSeconds(10);

    private readonly IHardwareMonitorService _hardware;
    private readonly IAdminElevationService _elevation;
    private readonly ISensorHistoryService _sensorHistory;
    private readonly IAlertRuleEngine _alertRules;
    private readonly IAnomalyDetector _anomalyDetector;
    private readonly DispatcherTimer _timer;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;
    private DateTime _lastAnomalyUtc = DateTime.MinValue;
    private int _persistGeneration;
    private int _anomalyGeneration;

    public MonitorViewModel(
        IHardwareMonitorService hardware,
        IAdminElevationService elevation,
        ISensorHistoryService sensorHistory,
        IAlertRuleEngine alertRules,
        IAnomalyDetector anomalyDetector)
    {
        _hardware = hardware;
        _elevation = elevation;
        _sensorHistory = sensorHistory;
        _alertRules = alertRules;
        _anomalyDetector = anomalyDetector;
        IsElevated = elevation.IsElevated;

        // 2s strikes a balance between live feedback and a low CPU footprint.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<SensorReading> Sensors { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdminElevationHint))]
    [NotifyPropertyChangedFor(nameof(ShowSensorStatusHint))]
    private bool _isElevated;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAdminElevationHint))]
    [NotifyPropertyChangedFor(nameof(ShowSensorStatusHint))]
    private bool _sensorsLimited;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSensorStatusHint))]
    private bool _monitorUnavailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSensorStatusHint))]
    private string? _statusMessage;

    public bool ShowAdminElevationHint => SensorsLimited && !IsElevated;

    public bool ShowSensorStatusHint =>
        !MonitorUnavailable
        && !ShowAdminElevationHint
        && !string.IsNullOrWhiteSpace(StatusMessage);

    [ObservableProperty] private string _cpuName = "CPU";
    [ObservableProperty] private string _cpuTempText = "n/a";
    [ObservableProperty] private string _cpuLoadText = "n/a";
    [ObservableProperty] private double _cpuLoadPercent;
    [ObservableProperty] private double _cpuTempPercent;
    [ObservableProperty] private string _cpuExtraText = string.Empty;

    [ObservableProperty] private string _gpuName = "GPU";
    [ObservableProperty] private string _gpuTempText = "n/a";
    [ObservableProperty] private string _gpuLoadText = "n/a";
    [ObservableProperty] private double _gpuLoadPercent;
    [ObservableProperty] private double _gpuTempPercent;
    [ObservableProperty] private string _gpuExtraText = string.Empty;

    [ObservableProperty] private string _ramText = "n/a";
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramDetailText = string.Empty;

    [ObservableProperty] private bool _hasActiveAlerts;
    [ObservableProperty] private string _alertSummaryText = string.Empty;
    [ObservableProperty] private string _alertDetailText = string.Empty;
    [ObservableProperty] private string _alertSeverityLabel = string.Empty;

    [ObservableProperty] private bool _hasAnomaly;
    [ObservableProperty] private string _anomalySummaryText = string.Empty;
    [ObservableProperty] private string _anomalyDetailText = string.Empty;
    [ObservableProperty] private string _anomalyConfidenceText = string.Empty;

    public void OnNavigatedTo()
    {
        Refresh();
        _timer.Start();
    }

    public void OnNavigatedFrom() => _timer.Stop();

    [RelayCommand]
    private void RunAsAdmin() => _elevation.RestartElevated();

    private void Refresh()
    {
        var s = _hardware.Capture();

        MonitorUnavailable = s.MonitorUnavailable;
        SensorsLimited = s.SensorsLimited;
        StatusMessage = s.StatusMessage;
        IsElevated = _elevation.IsElevated;

        CpuName = s.CpuName;
        CpuTempText = Temp(s.CpuTemperatureC);
        CpuTempPercent = Clamp(s.CpuTemperatureC);
        CpuLoadText = Percent(s.CpuLoadPercent);
        CpuLoadPercent = s.CpuLoadPercent ?? 0;
        CpuExtraText = BuildExtra(
            s.CpuClockMhz is { } mhz ? $"{mhz:F0} MHz" : null,
            s.CpuPowerWatts is { } w ? $"{w:F0} W" : null);

        GpuName = s.GpuName;
        GpuTempText = Temp(s.GpuTemperatureC);
        GpuTempPercent = Clamp(s.GpuTemperatureC);
        GpuLoadText = Percent(s.GpuLoadPercent);
        GpuLoadPercent = s.GpuLoadPercent ?? 0;
        GpuExtraText = BuildExtra(
            s.GpuMemoryUsedMb is { } used && s.GpuMemoryTotalMb is { } total
                ? $"{used / 1024:F1}/{total / 1024:F1} GB VRAM"
                : null,
            s.GpuPowerWatts is { } gw ? $"{gw:F0} W" : null);

        if (s is { RamUsedGb: { } ru, RamTotalGb: { } rt } && rt > 0)
        {
            RamText = $"{ru:F1} / {rt:F1} GB";
            RamPercent = s.RamLoadPercent ?? ru / rt * 100;
            RamDetailText = $"{Math.Max(0, rt - ru):F1} GB free";
        }
        else
        {
            RamText = "n/a";
            RamPercent = 0;
            RamDetailText = string.Empty;
        }

        UpdateSensors(s.Sensors);
        ApplyAlertSummary(_alertRules.Evaluate(s));
        MaybePersistSnapshot(s);
        MaybeRefreshAnomalies();
    }

    private void ApplyAlertSummary(IReadOnlyList<Alert> alerts)
    {
        HasActiveAlerts = alerts.Count > 0;
        if (!HasActiveAlerts)
        {
            AlertSummaryText = string.Empty;
            AlertDetailText = string.Empty;
            AlertSeverityLabel = string.Empty;
            return;
        }

        var highest = alerts.Max(a => a.Severity);
        var top = alerts.OrderByDescending(a => a.Severity).First();
        AlertSeverityLabel = highest.ToString();
        AlertSummaryText = $"{alerts.Count} active · {highest}";
        AlertDetailText = $"{top.Message} — {top.RecommendedAction}";
    }

    private void MaybeRefreshAnomalies()
    {
        var now = DateTime.UtcNow;
        // First paint always scores; afterwards match snapshot cadence (~10s).
        if (_lastAnomalyUtc != DateTime.MinValue && now - _lastAnomalyUtc < AnomalyInterval)
        {
            return;
        }

        _lastAnomalyUtc = now;
        var generation = Interlocked.Increment(ref _anomalyGeneration);
        _ = RefreshAnomaliesAsync(generation);
    }

    private async Task RefreshAnomaliesAsync(int generation)
    {
        IReadOnlyList<AnomalyHit> hits = [];
        try
        {
            var history = await _sensorHistory.GetRecentAsync(take: 120);
            if (generation != Volatile.Read(ref _anomalyGeneration))
            {
                return;
            }

            hits = await Task.Run(() => _anomalyDetector.Detect(history));
        }
        catch
        {
            // Anomaly scoring must not break the live monitor loop.
        }

        if (generation != Volatile.Read(ref _anomalyGeneration))
        {
            return;
        }

        ApplyAnomalySummary(hits);
    }

    private void ApplyAnomalySummary(IReadOnlyList<AnomalyHit> hits)
    {
        HasAnomaly = hits.Count > 0;
        if (!HasAnomaly)
        {
            AnomalySummaryText = string.Empty;
            AnomalyDetailText = string.Empty;
            AnomalyConfidenceText = string.Empty;
            return;
        }

        var top = hits
            .OrderByDescending(h => h.ConfidencePercent)
            .ThenByDescending(h => h.Severity)
            .First();
        AnomalySummaryText = $"{hits.Count} ML anomaly · {top.DetectorName}";
        AnomalyConfidenceText = $"{top.ConfidencePercent:F0}%";
        AnomalyDetailText = top.Explanation;
    }

    private void MaybePersistSnapshot(HardwareSnapshot snapshot)
    {
        if (snapshot.MonitorUnavailable)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastSnapshotUtc < SnapshotInterval)
        {
            return;
        }

        _lastSnapshotUtc = now;
        var generation = Interlocked.Increment(ref _persistGeneration);
        _ = PersistSnapshotAsync(snapshot, generation);
    }

    private async Task PersistSnapshotAsync(HardwareSnapshot snapshot, int generation)
    {
        try
        {
            await _sensorHistory.SaveSnapshotAsync(snapshot);
        }
        catch
        {
            // History failures must not break the live monitor loop.
        }

        _ = generation;
    }

    private void UpdateSensors(IReadOnlyList<SensorReading> readings)
    {
        Sensors.Clear();
        foreach (var reading in readings
                     .Where(r => r.Kind is SensorKind.Temperature or SensorKind.Fan or SensorKind.Power)
                     .OrderBy(r => r.Hardware)
                     .ThenBy(r => r.Name))
        {
            Sensors.Add(reading);
        }
    }

    private static string Temp(double? value) => value is { } v ? $"{v:F0} °C" : "n/a";
    private static string Percent(double? value) => value is { } v ? $"{v:F0} %" : "n/a";
    private static double Clamp(double? value) => value is { } v ? Math.Clamp(v, 0, 100) : 0;

    private static string BuildExtra(string? a, string? b)
    {
        var parts = new[] { a, b }.Where(p => !string.IsNullOrEmpty(p));
        return string.Join("   ·   ", parts);
    }
}
