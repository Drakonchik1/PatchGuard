using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.History;
using PatchGuard.Services.Optimization;

namespace PatchGuard.ViewModels;

public partial class OptimizeViewModel : ObservableObject, INavigationAware
{
    private readonly ISystemOptimizerService _optimizer;
    private readonly IPerformanceHistoryService _history;

    public OptimizeViewModel(ISystemOptimizerService optimizer, IPerformanceHistoryService history)
    {
        _optimizer = optimizer;
        _history = history;
    }

    public ObservableCollection<OptimizationStepResult> Steps { get; } = [];
    public ObservableCollection<OptimizationRunRecord> RecentRuns { get; } = [];

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _includeExplorerRestart;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _totalFreedText = "—";

    public void OnNavigatedTo()
    {
        PreviewSteps();
        _ = LoadHistoryAsync();
    }

    partial void OnIncludeExplorerRestartChanged(bool value)
    {
        if (!IsRunning)
        {
            PreviewSteps();
        }
    }

    private void PreviewSteps()
    {
        Steps.Clear();
        foreach (var step in _optimizer.GetSteps(IncludeExplorerRestart))
        {
            Steps.Add(new OptimizationStepResult
            {
                StepName = step.Name,
                Status = OptimizationStatus.Pending,
                Detail = step.Description
            });
        }

        HasResult = false;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task OptimizeAsync()
    {
        IsRunning = true;
        OptimizeCommand.NotifyCanExecuteChanged();
        PreviewSteps();
        SummaryText = "Optimizing…";

        var progress = new Progress<OptimizationStepResult>(OnStepProgress);

        try
        {
            var summary = await _optimizer.RunAsync(IncludeExplorerRestart, progress);
            TotalFreedText = OptimizationStepResult.FormatBytes(summary.TotalBytesFreed);
            SummaryText = $"Done — {summary.SucceededCount} of {summary.Steps.Count} step(s) succeeded, {TotalFreedText} reclaimed.";
            HasResult = true;
            await _history.SaveOptimizationAsync(summary);
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            SummaryText = $"Optimization error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            OptimizeCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !IsRunning;

    private void OnStepProgress(OptimizationStepResult update)
    {
        var existing = Steps.FirstOrDefault(s => s.StepName == update.StepName);
        if (existing is null)
        {
            Steps.Add(update);
            return;
        }

        var index = Steps.IndexOf(existing);
        Steps[index] = update;
    }

    private async Task LoadHistoryAsync()
    {
        RecentRuns.Clear();
        var items = await _history.GetRecentOptimizationsAsync();
        foreach (var item in items)
        {
            RecentRuns.Add(item);
        }

        HasHistory = RecentRuns.Count > 0;
    }
}
