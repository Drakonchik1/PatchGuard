using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.History;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public partial class FpsViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly IFpsCaptureService _fps;
    private readonly IPerformanceHistoryService _history;
    private readonly IAdminElevationService _elevation;
    private NavigationLifecycle? _lifecycle;
    private Task _loadHistoryTask = Task.CompletedTask;
    private Task _captureTask = Task.CompletedTask;
    private int _lifecycleGeneration;

    public FpsViewModel(
        IFpsCaptureService fps,
        IPerformanceHistoryService history,
        IAdminElevationService elevation)
    {
        _fps = fps;
        _history = history;
        _elevation = elevation;
        IsAvailable = fps.IsAvailable;
        IsElevated = elevation.IsElevated;
    }

    public ObservableCollection<GameProcessInfo> Processes { get; } = [];
    public ObservableCollection<FpsCaptureRecord> RecentCaptures { get; } = [];
    public int[] SecondsOptions { get; } = [5, 10, 15, 30, 60];

    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _hasHistory;

    [ObservableProperty] private GameProcessInfo? _selectedProcess;
    [ObservableProperty] private int _selectedSeconds = 10;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultTitle = string.Empty;
    [ObservableProperty] private string _averageFpsText = "—";
    [ObservableProperty] private string _onePercentLowText = "—";
    [ObservableProperty] private string _pointOnePercentLowText = "—";
    [ObservableProperty] private string _resultDetail = string.Empty;

    public void OnNavigatedTo()
    {
        RetireLifecycle();
        var lifecycle = new NavigationLifecycle();
        _lifecycle = lifecycle;
        var generation = Interlocked.Increment(ref _lifecycleGeneration);
        IsAvailable = _fps.IsAvailable;
        IsElevated = _elevation.IsElevated;
        StatusMessage = IsAvailable
            ? null
            : _fps.UnavailableReason
              ?? "PresentMon was not found. Add PresentMon-x64.exe to Tools\\PresentMon (see README.txt) to capture real game FPS.";
        RefreshProcesses();
        if (lifecycle.TryAcquire(out var lease))
        {
            _loadHistoryTask = ActiveTaskTracker.Retain(
                _loadHistoryTask,
                LoadHistoryAsync(generation, lease!));
        }
    }

    public void OnNavigatedFrom()
    {
        RetireLifecycle();
        IsCapturing = false;
        CaptureCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RefreshProcesses()
    {
        var previous = SelectedProcess?.ProcessId;
        Processes.Clear();
        foreach (var process in _fps.GetCandidateProcesses())
        {
            Processes.Add(process);
        }

        SelectedProcess = Processes.FirstOrDefault(p => p.ProcessId == previous) ?? Processes.FirstOrDefault();
    }

    [RelayCommand]
    private void RunAsAdmin() => _elevation.RestartElevated();

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureAsync()
    {
        if (SelectedProcess is not { } target ||
            _lifecycle is not { } lifecycle ||
            !lifecycle.TryAcquire(out var lease))
        {
            return Task.CompletedTask;
        }

        var generation = Volatile.Read(ref _lifecycleGeneration);
        IsCapturing = true;
        CaptureCommand.NotifyCanExecuteChanged();
        HasResult = false;
        StatusMessage = $"Capturing {target.ProcessName} for {SelectedSeconds}s — keep the game in focus and rendering…";
        var captureTask = CaptureCoreAsync(target, generation, lease!);
        _captureTask = ActiveTaskTracker.Retain(_captureTask, captureTask);
        return captureTask;
    }

    private async Task CaptureCoreAsync(
        GameProcessInfo target,
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        var cancellationToken = lease.CancellationToken;
        try
        {
            var result = await _fps.CaptureAsync(
                target,
                SelectedSeconds,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, cancellationToken))
            {
                return;
            }

            if (result.Success)
            {
                ResultTitle = result.ProcessName;
                AverageFpsText = $"{result.AverageFps:F0}";
                OnePercentLowText = $"{result.OnePercentLowFps:F0}";
                PointOnePercentLowText = $"{result.PointOnePercentLowFps:F0}";
                ResultDetail = result.Message;
                HasResult = true;
                StatusMessage = null;
                await _history.SaveFpsAsync(result, cancellationToken);
                var historyTask = LoadHistoryCoreAsync(
                    generation,
                    cancellationToken);
                _loadHistoryTask = ActiveTaskTracker.Retain(
                    _loadHistoryTask,
                    historyTask);
                await historyTask;
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, cancellationToken))
            {
                StatusMessage = $"Capture failed: {ex.Message}";
            }
        }
        finally
        {
            if (IsCurrent(generation, cancellationToken))
            {
                IsCapturing = false;
                CaptureCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool CanCapture() => !IsCapturing && IsAvailable;

    partial void OnSelectedProcessChanged(GameProcessInfo? value) => CaptureCommand.NotifyCanExecuteChanged();
    partial void OnIsAvailableChanged(bool value) => CaptureCommand.NotifyCanExecuteChanged();

    private async Task LoadHistoryAsync(
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        await LoadHistoryCoreAsync(generation, lease.CancellationToken);
    }

    private async Task LoadHistoryCoreAsync(
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await _history.GetRecentFpsAsync(
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, cancellationToken))
            {
                return;
            }

            RecentCaptures.Clear();
            foreach (var item in items)
            {
                RecentCaptures.Add(item);
            }

            HasHistory = RecentCaptures.Count > 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
    }

    private void RetireLifecycle()
    {
        Interlocked.Increment(ref _lifecycleGeneration);
        Interlocked.Exchange(ref _lifecycle, null)?.Retire();
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && generation == Volatile.Read(ref _lifecycleGeneration);
}
