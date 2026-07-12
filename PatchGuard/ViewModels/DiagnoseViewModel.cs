using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class DiagnoseViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;

    public DiagnoseViewModel(INavigationService navigation, ScanSessionState session)
    {
        _navigation = navigation;
        _session = session;
        Scenarios = new ObservableCollection<ScenarioOption>(ScanScenarioWorkflow.CreateOptions());
    }

    public ObservableCollection<ScenarioOption> Scenarios { get; }

    [RelayCommand]
    private void StartScan(ScenarioOption? option) =>
        ScanScenarioWorkflow.TryStart(option, _session, _navigation);
}
