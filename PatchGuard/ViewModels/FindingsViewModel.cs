using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Performance;

namespace PatchGuard.ViewModels;

public partial class FindingsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IGameFpsService _gameFps;

    public FindingsViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IGameFpsService gameFps)
    {
        _navigation = navigation;
        _session = session;
        _gameFps = gameFps;
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
    private bool _isGamePerformanceScenario;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _gameFpsInput = string.Empty;

    [ObservableProperty]
    private string _gameFpsResult = string.Empty;

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

        ScenarioTitle = _session.SelectedScenario?.GetTitle() ?? "Results";
        WarningCount = Findings.Count(f => f.Severity >= FindingSeverity.Warning);
        HealthScore = Math.Clamp(100 - WarningCount * 12, 20, 100);
        IsGamePerformanceScenario = _session.SelectedScenario == ScanScenario.GamePerformanceCheck;
        GameFpsResult = string.Empty;
    }

    [RelayCommand]
    private async Task SaveGameFpsAsync()
    {
        if (string.IsNullOrWhiteSpace(GameName) || !int.TryParse(GameFpsInput, out var fps) || fps <= 0)
        {
            GameFpsResult = "Enter game name and FPS (number).";
            return;
        }

        var result = await _gameFps.SaveAndCompareAsync(GameName, fps);
        GameFpsResult = result.Summary;

        Findings.Add(new Finding
        {
            ModuleName = "Game FPS",
            Title = $"{GameName.Trim()}: {fps} FPS",
            Details = result.Summary,
            Severity = result.PreviousFps is int prev && fps < prev * 0.85
                ? FindingSeverity.Warning
                : FindingSeverity.Info
        });
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
