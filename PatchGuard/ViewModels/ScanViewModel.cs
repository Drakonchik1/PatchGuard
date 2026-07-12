using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class ScanViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IDiagnosticOrchestrator _orchestrator;
    private readonly IScanHistoryService _history;
    private CancellationTokenSource? _scanCts;
    private bool _scanStarted;

    public ScanViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IDiagnosticOrchestrator orchestrator,
        IScanHistoryService history)
    {
        _navigation = navigation;
        _session = session;
        _orchestrator = orchestrator;
        _history = history;
    }

    public ObservableCollection<DiagnosticProgressItem> ProgressItems { get; } = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scenarioTitle = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public void OnNavigatedTo()
    {
        if (_scanStarted)
        {
            return;
        }

        _scanStarted = true;
        _ = RunScanAsync();
    }

    public void OnNavigatedFrom() => _scanCts?.Cancel();

    private async Task RunScanAsync()
    {
        if (_session.SelectedScenario is not ScanScenario scenario)
        {
            ErrorMessage = "No scan scenario selected.";
            return;
        }

        ScenarioTitle = scenario.GetTitle();
        ProgressItems.Clear();
        _session.ProgressItems.Clear();
        _session.Findings.Clear();
        ErrorMessage = null;
        IsScanning = true;

        using var scanCts = new CancellationTokenSource();
        _scanCts = scanCts;

        try
        {
            var modules = _orchestrator.GetModulesForScenario(scenario);
            foreach (var module in modules)
            {
                var pending = new DiagnosticProgressItem
                {
                    ModuleName = module.Name,
                    Status = DiagnosticProgressStatus.Pending,
                    Message = module.Description
                };
                ProgressItems.Add(pending);
                _session.ProgressItems.Add(pending);
            }

            var progress = new Progress<DiagnosticProgressItem>(UpdateProgress);
            var findings = await _orchestrator.RunScanAsync(scenario, progress, scanCts.Token);
            scanCts.Token.ThrowIfCancellationRequested();

            _session.Findings.AddRange(findings);
            await _history.SaveScanAsync(scenario, findings, scanCts.Token);
            scanCts.Token.ThrowIfCancellationRequested();

            _navigation.NavigateTo<FindingsViewModel>();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
            if (ReferenceEquals(_scanCts, scanCts))
            {
                _scanCts = null;
            }
        }
    }

    private void UpdateProgress(DiagnosticProgressItem update)
    {
        var existing = ProgressItems.FirstOrDefault(p => p.ModuleName == update.ModuleName);
        if (existing is null)
        {
            ProgressItems.Add(update);
            return;
        }

        existing.Status = update.Status;
        existing.Message = update.Message;
    }

    [RelayCommand]
    private void CancelScan()
    {
        _scanCts?.Cancel();
        _navigation.GoBack();
    }

    [RelayCommand]
    private void GoBack()
    {
        _scanCts?.Cancel();
        _navigation.GoBack();
    }
}
