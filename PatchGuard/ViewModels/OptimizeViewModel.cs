using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.History;
using PatchGuard.Services.Optimization;

namespace PatchGuard.ViewModels;

public partial class OptimizeViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly ISystemOptimizerService _optimizer;
    private readonly IPerformanceHistoryService _history;
    private NavigationLifecycle? _lifecycle;
    private Task _loadHistoryTask = Task.CompletedTask;
    private Task _optimizeTask = Task.CompletedTask;
    private int _lifecycleGeneration;

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
        RetireLifecycle();
        var lifecycle = new NavigationLifecycle();
        _lifecycle = lifecycle;
        var generation = Interlocked.Increment(ref _lifecycleGeneration);
        PreviewSteps();
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
        IsRunning = false;
        OptimizeCommand.NotifyCanExecuteChanged();
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
    private Task OptimizeAsync()
    {
        if (_lifecycle is null)
        {
            return Task.CompletedTask;
        }

        PreviewSteps();
        IsRunning = true;
        OptimizeCommand.NotifyCanExecuteChanged();
        SummaryText = "Optimizing…";
        if (_lifecycle is not { } lifecycle ||
            !lifecycle.TryAcquire(out var lease))
        {
            IsRunning = false;
            OptimizeCommand.NotifyCanExecuteChanged();
            return Task.CompletedTask;
        }

        var generation = Volatile.Read(ref _lifecycleGeneration);
        var optimizeTask = OptimizeCoreAsync(generation, lease!);
        _optimizeTask = ActiveTaskTracker.Retain(
            _optimizeTask,
            optimizeTask);
        return optimizeTask;
    }

    private async Task OptimizeCoreAsync(
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        var cancellationToken = lease.CancellationToken;
        var progress = new Progress<OptimizationStepResult>(update =>
        {
            if (IsCurrent(generation, cancellationToken))
            {
                OnStepProgress(update);
            }
        });

        try
        {
            var summary = await _optimizer.RunAsync(
                IncludeExplorerRestart,
                progress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, cancellationToken))
            {
                return;
            }

            TotalFreedText = OptimizationStepResult.FormatBytes(summary.TotalBytesFreed);
            SummaryText = $"Done — {summary.SucceededCount} of {summary.Steps.Count} step(s) succeeded, {TotalFreedText} reclaimed.";
            HasResult = true;
            await _history.SaveOptimizationAsync(summary, cancellationToken);
            var historyTask = LoadHistoryCoreAsync(
                generation,
                cancellationToken);
            _loadHistoryTask = ActiveTaskTracker.Retain(
                _loadHistoryTask,
                historyTask);
            await historyTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, cancellationToken))
            {
                SummaryText = $"Optimization error: {ex.Message}";
            }
        }
        finally
        {
            if (IsCurrent(generation, cancellationToken))
            {
                IsRunning = false;
                OptimizeCommand.NotifyCanExecuteChanged();
            }
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
            var items = await _history.GetRecentOptimizationsAsync(
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, cancellationToken))
            {
                return;
            }

            RecentRuns.Clear();
            foreach (var item in items)
            {
                RecentRuns.Add(item);
            }

            HasHistory = RecentRuns.Count > 0;
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
