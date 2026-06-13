using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.History;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public partial class FpsViewModel : ObservableObject, INavigationAware
{
    private readonly IFpsCaptureService _fps;
    private readonly IPerformanceHistoryService _history;
    private readonly IAdminElevationService _elevation;

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
        IsAvailable = _fps.IsAvailable;
        IsElevated = _elevation.IsElevated;
        StatusMessage = IsAvailable
            ? null
            : _fps.UnavailableReason
              ?? "PresentMon was not found. Add PresentMon-x64.exe to Tools\\PresentMon (see README.txt) to capture real game FPS.";
        RefreshProcesses();
        _ = LoadHistoryAsync();
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
    private async Task CaptureAsync()
    {
        if (SelectedProcess is not { } target)
        {
            return;
        }

        IsCapturing = true;
        CaptureCommand.NotifyCanExecuteChanged();
        HasResult = false;
        StatusMessage = $"Capturing {target.ProcessName} for {SelectedSeconds}s — keep the game in focus and rendering…";

        try
        {
            var result = await _fps.CaptureAsync(target, SelectedSeconds);
            if (result.Success)
            {
                ResultTitle = result.ProcessName;
                AverageFpsText = $"{result.AverageFps:F0}";
                OnePercentLowText = $"{result.OnePercentLowFps:F0}";
                PointOnePercentLowText = $"{result.PointOnePercentLowFps:F0}";
                ResultDetail = result.Message;
                HasResult = true;
                StatusMessage = null;
                await _history.SaveFpsAsync(result);
                await LoadHistoryAsync();
            }
            else
            {
                StatusMessage = result.Message;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Capture failed: {ex.Message}";
        }
        finally
        {
            IsCapturing = false;
            CaptureCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCapture() => !IsCapturing && IsAvailable;

    partial void OnSelectedProcessChanged(GameProcessInfo? value) => CaptureCommand.NotifyCanExecuteChanged();
    partial void OnIsAvailableChanged(bool value) => CaptureCommand.NotifyCanExecuteChanged();

    private async Task LoadHistoryAsync()
    {
        RecentCaptures.Clear();
        var items = await _history.GetRecentFpsAsync();
        foreach (var item in items)
        {
            RecentCaptures.Add(item);
        }

        HasHistory = RecentCaptures.Count > 0;
    }
}
