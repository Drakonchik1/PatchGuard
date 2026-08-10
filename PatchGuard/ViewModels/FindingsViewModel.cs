using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Health;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class FindingsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IHealthScorePolicy _healthScorePolicy;

    public FindingsViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IHealthScorePolicy healthScorePolicy)
    {
        _navigation = navigation;
        _session = session;
        _healthScorePolicy = healthScorePolicy;
    }

    public ObservableCollection<Finding> Findings { get; } = [];
    public ObservableCollection<ScanMetric> ScanMetrics { get; } = [];

    [ObservableProperty]
    private string _scenarioTitle = string.Empty;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _healthScore;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _totalFindings;

    public string HealthStatusLabel => HealthScore switch
    {
        >= 85 => "Healthy",
        >= 70 => "Fair",
        >= 50 => "Needs attention",
        _ => "Critical issues detected"
    };

    public string HealthStatusDetail
    {
        get
        {
            if (WarningCount == 0 && CriticalCount == 0)
            {
                return "No warnings or critical issues in this scan.";
            }

            var parts = new List<string>();
            if (WarningCount > 0)
            {
                parts.Add($"{WarningCount} warning{(WarningCount == 1 ? string.Empty : "s")}");
            }

            if (CriticalCount > 0)
            {
                parts.Add($"{CriticalCount} critical");
            }

            return $"{string.Join(" and ", parts)} to review below.";
        }
    }

    public bool HasSystemMetrics => ScanMetrics.Count > 0;

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
        TotalFindings = Findings.Count;
        WarningCount = Findings.Count(f => f.Severity >= FindingSeverity.Warning);
        CriticalCount = Findings.Count(f => f.Severity == FindingSeverity.Critical);
        HealthScore = _healthScorePolicy.Calculate(_session.Findings);
        OnPropertyChanged(nameof(HealthStatusLabel));
        OnPropertyChanged(nameof(HealthStatusDetail));
        OnPropertyChanged(nameof(HasSystemMetrics));
    }

    partial void OnHealthScoreChanged(int value)
    {
        OnPropertyChanged(nameof(HealthStatusLabel));
        OnPropertyChanged(nameof(HealthStatusDetail));
    }

    partial void OnWarningCountChanged(int value) => OnPropertyChanged(nameof(HealthStatusDetail));

    partial void OnCriticalCountChanged(int value) => OnPropertyChanged(nameof(HealthStatusDetail));

    [RelayCommand]
    private void GetRepairGuide()
    {
        _session.Guide = null;
        _navigation.NavigateTo<GuideViewModel>();
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();
}
