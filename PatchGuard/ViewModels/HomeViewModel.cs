using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class HomeViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IScanHistoryService _history;

    public HomeViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IScanHistoryService history)
    {
        _navigation = navigation;
        _session = session;
        _history = history;

        Scenarios =
        [
            new ScenarioOption { Scenario = ScanScenario.FullSystemAudit },
            new ScenarioOption { Scenario = ScanScenario.GamePerformanceCheck },
            new ScenarioOption { Scenario = ScanScenario.AfterWindowsUpdate },
            new ScenarioOption { Scenario = ScanScenario.QuickHealthCheck }
        ];
    }

    public ObservableCollection<ScenarioOption> Scenarios { get; }
    public ObservableCollection<ScanHistoryEntry> RecentScans { get; } = [];

    [ObservableProperty]
    private string _machineLabel = Environment.MachineName;

    [ObservableProperty]
    private bool _hasScanHistory;

    public void OnNavigatedTo() => _ = LoadHistoryAsync();

    private async Task LoadHistoryAsync()
    {
        RecentScans.Clear();
        var items = await _history.GetRecentScansAsync();
        foreach (var item in items)
        {
            RecentScans.Add(item);
        }

        HasScanHistory = RecentScans.Count > 0;
    }

    [RelayCommand]
    private void StartScan(ScenarioOption? option)
    {
        if (option is null)
        {
            return;
        }

        _session.Reset();
        _session.SelectedScenario = option.Scenario;
        _navigation.NavigateTo<ScanViewModel>();
    }
}
