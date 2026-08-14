using System.Collections.ObjectModel;
using System.Windows;
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
    private static readonly SemaphoreSlim CaptureGate = new(1, 1);
    private static readonly SemaphoreSlim PersistenceGate = new(1, 1);

    private readonly IHardwareMonitorService _hardware;
    private readonly IAdminElevationService _elevation;
    private readonly ISensorHistoryService _sensorHistory;
    private readonly IAlertRuleEngine _alertRules;
    private readonly IAnomalyDetector _anomalyDetector;
    private DateTime _lastSnapshotUtc = DateTime.MinValue;
    private DateTime _lastAnomalyUtc = DateTime.MinValue;
    private NavigationLifecycle? _lifecycle;
    private Task _monitorLoopTask = Task.CompletedTask;
    private Task _persistenceTask = Task.CompletedTask;
    private Task _anomalyTask = Task.CompletedTask;
    private int _lifecycleGeneration;

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
        OnNavigatedFrom();
        var lifecycle = new NavigationLifecycle();
        _lifecycle = lifecycle;
        var generation = Interlocked.Increment(ref _lifecycleGeneration);
        var dispatcher = CaptureOwningDispatcher();
        if (lifecycle.TryAcquire(out var lease))
        {
            var loopTask = RunMonitorLoopAsync(
                generation,
                lifecycle,
                lease!,
                dispatcher);
            _monitorLoopTask = ActiveTaskTracker.Retain(
                _monitorLoopTask,
                loopTask);
        }
    }

    public void OnNavigatedFrom()
    {
        Interlocked.Increment(ref _lifecycleGeneration);
        Interlocked.Exchange(ref _lifecycle, null)?.Retire();
    }

    [RelayCommand]
    private void RunAsAdmin() => _elevation.RestartElevated();

    private async Task RunMonitorLoopAsync(
        int generation,
        NavigationLifecycle lifecycle,
        NavigationLifecycleLease lease,
        Dispatcher? dispatcher)
    {
        using var lifecycleLease = lease;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var cancellationToken = lease.CancellationToken;
        try
        {
            while (true)
            {
                try
                {
                    var snapshot = await CaptureSnapshotAsync(cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrent(generation, cancellationToken))
                    {
                        return;
                    }

                    var alerts = _alertRules.Evaluate(snapshot);
                    MaybePersistSnapshot(
                        snapshot,
                        generation,
                        lifecycle);
                    MaybeRefreshAnomalies(
                        generation,
                        lifecycle,
                        dispatcher);
                    await PostToUiAsync(
                        dispatcher,
                        () => ApplySnapshot(snapshot, alerts),
                        generation,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    await TryPostStatusAsync(
                        dispatcher,
                        $"Monitor refresh failed: {ex.Message}",
                        generation,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken)
                        .ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch (Exception ex)
        {
            await TryPostStatusAsync(
                dispatcher,
                $"Monitor stopped after an unexpected error: {ex.Message}",
                generation,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HardwareSnapshot> CaptureSnapshotAsync(
        CancellationToken cancellationToken)
    {
        await CaptureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await Task.Run(_hardware.Capture).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        finally
        {
            CaptureGate.Release();
        }
    }

    private void ApplySnapshot(HardwareSnapshot s, IReadOnlyList<Alert> alerts)
    {
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
        ApplyAlertSummary(alerts);
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

    private void MaybeRefreshAnomalies(
        int generation,
        NavigationLifecycle lifecycle,
        Dispatcher? dispatcher)
    {
        var now = DateTime.UtcNow;
        // First paint always scores; afterwards match snapshot cadence (~10s).
        if (_lastAnomalyUtc != DateTime.MinValue && now - _lastAnomalyUtc < AnomalyInterval)
        {
            return;
        }

        if (!_anomalyTask.IsCompleted ||
            !lifecycle.TryAcquire(out var lease))
        {
            return;
        }

        _lastAnomalyUtc = now;
        _anomalyTask = RefreshAnomaliesAsync(
            generation,
            lease!,
            dispatcher);
    }

    private async Task RefreshAnomaliesAsync(
        int generation,
        NavigationLifecycleLease lease,
        Dispatcher? dispatcher)
    {
        using var lifecycleLease = lease;
        var cancellationToken = lease.CancellationToken;
        IReadOnlyList<AnomalyHit> hits = [];
        try
        {
            var history = await _sensorHistory
                .GetRecentAsync(take: 120, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            hits = _anomalyDetector.Detect(history);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // Anomaly scoring must not break the live monitor loop.
        }

        try
        {
            await PostToUiAsync(
                dispatcher,
                () => ApplyAnomalySummary(hits),
                generation,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch
        {
            // Dispatcher shutdown or a UI callback failure cannot fault this task.
        }
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

    private void MaybePersistSnapshot(
        HardwareSnapshot snapshot,
        int generation,
        NavigationLifecycle lifecycle)
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

        if (!_persistenceTask.IsCompleted ||
            !lifecycle.TryAcquire(out var lease))
        {
            return;
        }

        _lastSnapshotUtc = now;
        _persistenceTask = PersistSnapshotAsync(
            snapshot,
            generation,
            lease!);
    }

    private async Task PersistSnapshotAsync(
        HardwareSnapshot snapshot,
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        var cancellationToken = lease.CancellationToken;
        var gateHeld = false;
        try
        {
            await PersistenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            if (IsCurrent(generation, cancellationToken))
            {
                await _sensorHistory
                    .SaveSnapshotAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch
        {
            // History failures must not break the live monitor loop.
        }
        finally
        {
            if (gateHeld)
            {
                PersistenceGate.Release();
            }
        }
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && generation == Volatile.Read(ref _lifecycleGeneration);

    private async Task PostToUiAsync(
        Dispatcher? dispatcher,
        Action update,
        int generation,
        CancellationToken cancellationToken)
    {
        if (!IsCurrent(generation, cancellationToken))
        {
            return;
        }

        if (dispatcher is null ||
            dispatcher.HasShutdownStarted ||
            dispatcher.HasShutdownFinished)
        {
            return;
        }

        await dispatcher.InvokeAsync(
            () =>
            {
                if (IsCurrent(generation, cancellationToken))
                {
                    update();
                }
            },
            DispatcherPriority.DataBind,
            cancellationToken);
    }

    private static Dispatcher? CaptureOwningDispatcher()
    {
        var applicationDispatcher = Application.Current?.Dispatcher;
        if (applicationDispatcher is null ||
            !applicationDispatcher.CheckAccess() ||
            applicationDispatcher.HasShutdownStarted ||
            applicationDispatcher.HasShutdownFinished)
        {
            return null;
        }

        return applicationDispatcher;
    }

    private async Task TryPostStatusAsync(
        Dispatcher? dispatcher,
        string message,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await PostToUiAsync(
                dispatcher,
                () => StatusMessage = message,
                generation,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch
        {
            // Fail closed when the owning dispatcher is unavailable.
        }
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
