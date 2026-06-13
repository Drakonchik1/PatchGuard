using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class FindingsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;

    public FindingsViewModel(INavigationService navigation, ScanSessionState session)
    {
        _navigation = navigation;
        _session = session;
    }

    public ObservableCollection<Finding> Findings { get; } = [];
    public ObservableCollection<ScanMetric> ScanMetrics { get; } = [];

    [ObservableProperty]
    private string _scenarioTitle = string.Empty;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _healthScore;

    public void OnNavigatedTo()
    {
        Findings.Clear();
        ScanMetrics.Clear();

        foreach (var finding in _session.Findings)
        {
            Findings.Add(finding);
        }

        foreach (var metric in ScanMetricBuilder.FromFindings(_session.Findings))
        {
            ScanMetrics.Add(metric);
        }

        ScenarioTitle = _session.SelectedScenario?.GetTitle() ?? "Scan results";
        WarningCount = Findings.Count(f => f.Severity >= FindingSeverity.Warning);
        HealthScore = Math.Clamp(100 - WarningCount * 12 - Findings.Count(f => f.Severity == FindingSeverity.Critical) * 20, 20, 100);
    }

    [RelayCommand]
    private void GetRepairGuide()
    {
        _session.Guide = null;
        _navigation.NavigateTo<GuideViewModel>();
    }

    [RelayCommand]
    private void GoBack() => _navigation.NavigateHome();
}
