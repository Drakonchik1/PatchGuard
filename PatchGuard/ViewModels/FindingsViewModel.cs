using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Fixes;
using PatchGuard.Services.Health;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public sealed class FindingItemViewModel
{
    public required Finding Finding { get; init; }
    public required bool CanRunSafeFix { get; init; }

    public string Title => Finding.Title;
    public string ModuleName => Finding.ModuleName;
    public FindingSeverity Severity => Finding.Severity;
    public string Explanation => Finding.Explanation;
    public string Evidence => Finding.Evidence;
    public string RecommendedFix => Finding.RecommendedFix;
    public FindingActionState ActionState => Finding.ActionState;
    public FindingAdminRequirement AdminRequirement => Finding.AdminRequirement;
    public FindingRisk Risk => Finding.Risk;
    public FindingVerificationStatus VerificationStatus => Finding.VerificationStatus;
}

public partial class FindingsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IHealthScorePolicy _healthScorePolicy;
    private readonly IGuidedFixPlanService _fixPlans;
    private readonly IUserConfirmationService _confirmation;

    public FindingsViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IHealthScorePolicy healthScorePolicy,
        IGuidedFixPlanService fixPlans,
        IUserConfirmationService confirmation)
    {
        _navigation = navigation;
        _session = session;
        _healthScorePolicy = healthScorePolicy;
        _fixPlans = fixPlans;
        _confirmation = confirmation;
    }

    public ObservableCollection<FindingItemViewModel> Findings { get; } = [];
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

    [ObservableProperty]
    private bool _isFixRunning;

    [ObservableProperty]
    private string? _fixStatusMessage;

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
        FixStatusMessage = null;

        var scenario = _session.SelectedScenario?.GetTitle();
        foreach (var finding in _session.Findings)
        {
            Findings.Add(new FindingItemViewModel
            {
                Finding = finding,
                CanRunSafeFix = _fixPlans.TryBuildFromFinding(finding, scenario) is not null
            });
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

    [RelayCommand(CanExecute = nameof(CanRunFix))]
    private async Task RunSafeFixAsync(FindingItemViewModel? item)
    {
        if (item is null || !item.CanRunSafeFix)
        {
            return;
        }

        var scenario = _session.SelectedScenario?.GetTitle();
        var plan = _fixPlans.TryBuildFromFinding(item.Finding, scenario);
        if (plan is null)
        {
            FixStatusMessage = "No safe automated fix is available for this finding.";
            return;
        }

        var preview = _fixPlans.Preview(plan);
        var confirmMessage =
            $"{preview.Summary}\n\n" +
            string.Join("\n", preview.StepSummaries.Select(s => "• " + s)) +
            "\n\nRun these safe steps now?";

        if (!_confirmation.Confirm("Confirm guided fix", confirmMessage))
        {
            FixStatusMessage = "Fix cancelled. No system changes were made.";
            return;
        }

        IsFixRunning = true;
        RunSafeFixCommand.NotifyCanExecuteChanged();
        FixStatusMessage = "Running safe fix…";
        try
        {
            var result = await _fixPlans.ExecuteAsync(plan, GuidedFixConfirmation.ConfirmNow());
            FixStatusMessage = result.Summary;
        }
        catch (Exception ex)
        {
            FixStatusMessage = $"Guided fix failed: {ex.Message}";
        }
        finally
        {
            IsFixRunning = false;
            RunSafeFixCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunFix(FindingItemViewModel? item) =>
        !IsFixRunning && item is { CanRunSafeFix: true };

    [RelayCommand]
    private void GetRepairGuide()
    {
        _session.Guide = null;
        _navigation.NavigateTo<GuideViewModel>();
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();
}
