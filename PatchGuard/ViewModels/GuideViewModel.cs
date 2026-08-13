using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Fixes;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public sealed class FixStepItemViewModel
{
    public required FixStep Step { get; init; }
    public required bool CanRunSafeFix { get; init; }

    public int Order => Step.Order;
    public string Title => Step.Title;
    public string Instructions => Step.Instructions;
    public string? CopyText => Step.CopyText;
    public string? LinkUrl => Step.LinkUrl;
    public string? WhyThisMatters => Step.WhyThisMatters;
    public string? Evidence => Step.Evidence;
}

public partial class GuideViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IAiCouncilService _aiCouncil;
    private readonly IGuidedFixPlanService _fixPlans;
    private readonly IUserConfirmationService _confirmation;
    private CancellationTokenSource? _councilCts;

    public GuideViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IAiCouncilService aiCouncil,
        IGuidedFixPlanService fixPlans,
        IUserConfirmationService confirmation)
    {
        _navigation = navigation;
        _session = session;
        _aiCouncil = aiCouncil;
        _fixPlans = fixPlans;
        _confirmation = confirmation;

        AgentPanels =
        [
            new AgentPanelState(CouncilAgents.Technician, "⚙"),
            new AgentPanelState(CouncilAgents.Skeptic, "⚠"),
            new AgentPanelState(CouncilAgents.Researcher, "⌕")
        ];

        PhaseSteps =
        [
            new CouncilPhaseStep { Name = "Analyze" },
            new CouncilPhaseStep { Name = "Research" },
            new CouncilPhaseStep { Name = "Debate" },
            new CouncilPhaseStep { Name = "Verdict" }
        ];
    }

    public ObservableCollection<AgentPanelState> AgentPanels { get; }
    public ObservableCollection<CouncilPhaseStep> PhaseSteps { get; }
    public ObservableCollection<CouncilMessage> CouncilMessages { get; } = [];
    public ObservableCollection<FixStepItemViewModel> FixSteps { get; } = [];
    public ObservableCollection<WebReference> WebReferences { get; } = [];
    public ObservableCollection<KnowledgeReference> KnowledgeReferences { get; } = [];
    public ObservableCollection<ScanMetric> ScanMetrics { get; } = [];
    public ObservableCollection<string> SourceLabels { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _chiefVerdict = string.Empty;

    [ObservableProperty]
    private string _detailedExplanation = string.Empty;

    [ObservableProperty]
    private string _councilStatus = string.Empty;

    [ObservableProperty]
    private bool _isCouncilRunning;

    [ObservableProperty]
    private bool _hasExternalAiConsent;

    [ObservableProperty]
    private int _healthScore;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isFixRunning;

    [ObservableProperty]
    private string? _fixStatusMessage;

    public void OnNavigatedTo()
    {
        ResetUi();
        LoadScanMetrics();

        if (_session.Guide is not null)
        {
            ApplyGuide(_session.Guide);
            MarkAllPhasesComplete();
            return;
        }

        Summary = "Optional guidance is ready when you choose to generate it.";
        SourceLabels.Add("Local diagnostic data");
    }

    public void OnNavigatedFrom()
    {
        _councilCts?.Cancel();
        IsCouncilRunning = false;
        RunCouncilCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunCouncil))]
    private async Task RunCouncilAsync()
    {
        ResetUi();

        if (_session.SelectedScenario is not ScanScenario scenario)
        {
            ErrorMessage = "No scan scenario selected.";
            return;
        }

        LoadScanMetrics();
        IsCouncilRunning = true;
        RunCouncilCommand.NotifyCanExecuteChanged();
        _councilCts?.Cancel();
        using var councilCts = new CancellationTokenSource();
        _councilCts = councilCts;

        try
        {
            var progress = new Progress<CouncilProgressUpdate>(HandleProgress);

            var guide = await _aiCouncil.BuildGuideAsync(
                scenario,
                _session.Findings,
                progress,
                councilCts.Token,
                HasExternalAiConsent);
            councilCts.Token.ThrowIfCancellationRequested();

            _session.Guide = guide;
            ApplyGuide(guide);
            MarkAllPhasesComplete();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "AI guidance cancelled. No system changes were made.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Council failed: {ex.Message}";
        }
        finally
        {
            IsCouncilRunning = false;
            HasExternalAiConsent = false;
            RunCouncilCommand.NotifyCanExecuteChanged();
            CouncilStatus = string.Empty;
            foreach (var panel in AgentPanels)
            {
                panel.IsActive = false;
            }

            if (ReferenceEquals(_councilCts, councilCts))
            {
                _councilCts = null;
            }
        }
    }

    private bool CanRunCouncil() => !IsCouncilRunning;

    private void ResetUi()
    {
        CouncilMessages.Clear();
        FixSteps.Clear();
        WebReferences.Clear();
        KnowledgeReferences.Clear();
        ScanMetrics.Clear();
        SourceLabels.Clear();
        ChiefVerdict = string.Empty;
        DetailedExplanation = string.Empty;
        Summary = string.Empty;
        HealthScore = 0;
        ErrorMessage = null;
        FixStatusMessage = null;

        foreach (var step in PhaseSteps)
        {
            step.IsComplete = false;
            step.IsCurrent = false;
        }

        PhaseSteps[0].IsCurrent = true;

        foreach (var panel in AgentPanels)
        {
            panel.PhaseLabel = "Idle";
            panel.Headline = "Standing by…";
            panel.Confidence = 0;
            panel.IsActive = false;
        }
    }

    private void LoadScanMetrics()
    {
        ScanMetrics.Clear();
        foreach (var metric in ScanMetricBuilder.FromFindings(_session.Findings))
        {
            ScanMetrics.Add(metric);
        }
    }

    private void HandleProgress(CouncilProgressUpdate update)
    {
        if (!string.IsNullOrWhiteSpace(update.StatusText))
        {
            CouncilStatus = update.StatusText;
        }

        if (update.Phase is not null)
        {
            UpdatePhaseStepper(update.Phase.Value);
        }

        if (update.Panel is not null)
        {
            var panel = AgentPanels.FirstOrDefault(p => p.Role == update.Panel.Role);
            if (panel is not null)
            {
                panel.PhaseLabel = update.Panel.PhaseLabel;
                panel.Headline = update.Panel.Headline;
                panel.Confidence = update.Panel.Confidence;
                panel.IsActive = update.Panel.IsActive;

                foreach (var other in AgentPanels.Where(p => p.Role != panel.Role))
                {
                    if (update.Panel.IsActive)
                    {
                        other.IsActive = false;
                    }
                }
            }
        }

        if (update.Message is not null)
        {
            CouncilMessages.Add(update.Message);
        }

        if (!string.IsNullOrWhiteSpace(update.ChiefVerdict))
        {
            ChiefVerdict = update.ChiefVerdict;
        }
    }

    private void UpdatePhaseStepper(CouncilPhaseType phase)
    {
        var index = phase switch
        {
            CouncilPhaseType.Analysis => 0,
            CouncilPhaseType.Research => 1,
            CouncilPhaseType.Debate or CouncilPhaseType.Rebuttal => 2,
            CouncilPhaseType.Verdict => 3,
            _ => 0
        };

        for (var i = 0; i < PhaseSteps.Count; i++)
        {
            PhaseSteps[i].IsComplete = i < index;
            PhaseSteps[i].IsCurrent = i == index;
        }
    }

    private void MarkAllPhasesComplete()
    {
        foreach (var step in PhaseSteps)
        {
            step.IsComplete = true;
            step.IsCurrent = false;
        }
    }

    private void ApplyGuide(RepairGuide guide)
    {
        Summary = guide.Summary;
        ChiefVerdict = guide.ChiefVerdict;
        DetailedExplanation = guide.DetailedExplanation ?? string.Empty;
        HealthScore = guide.HealthScore;
        SourceLabels.Clear();
        foreach (var source in guide.Sources.Distinct())
        {
            SourceLabels.Add(source switch
            {
                GuidanceSource.Local => "Local diagnostic data",
                GuidanceSource.AiGenerated =>
                    string.Equals(guide.AiProviderName, OllamaChatProvider.ProviderName, StringComparison.OrdinalIgnoreCase)
                        ? "Local LLM (Ollama)"
                        : "AI-generated advice",
                GuidanceSource.WebSourced => "Web-sourced research",
                GuidanceSource.KnowledgeBase => "Local knowledge base",
                _ => "Source unavailable"
            });
        }

        CouncilMessages.Clear();
        foreach (var message in guide.CouncilDiscussion)
        {
            CouncilMessages.Add(message);
        }

        var scenario = _session.SelectedScenario?.GetTitle();
        FixSteps.Clear();
        foreach (var step in guide.Steps.OrderBy(s => s.Order))
        {
            FixSteps.Add(new FixStepItemViewModel
            {
                Step = step,
                CanRunSafeFix = _fixPlans.TryBuildFromFixStep(step, scenario) is not null
            });
        }

        WebReferences.Clear();
        foreach (var reference in guide.WebReferences)
        {
            WebReferences.Add(reference);
        }

        KnowledgeReferences.Clear();
        foreach (var reference in guide.KnowledgeReferences)
        {
            KnowledgeReferences.Add(reference);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunFix))]
    private async Task RunSafeFixAsync(FixStepItemViewModel? item)
    {
        if (item is null || !item.CanRunSafeFix)
        {
            return;
        }

        var scenario = _session.SelectedScenario?.GetTitle();
        var plan = _fixPlans.TryBuildFromFixStep(item.Step, scenario);
        if (plan is null)
        {
            FixStatusMessage = "No safe automated fix is available for this guide step.";
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

    private bool CanRunFix(FixStepItemViewModel? item) =>
        !IsFixRunning && !IsCouncilRunning && item is { CanRunSafeFix: true };

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!LaunchUriPolicy.TryNormalize(url, out var launchUri) || launchUri is null)
        {
            ErrorMessage = "Blocked a link that was not a safe web or Windows Settings address.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(launchUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not open link: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
        }
    }

    [RelayCommand]
    private void CopyChiefVerdict()
    {
        if (!string.IsNullOrWhiteSpace(ChiefVerdict))
        {
            System.Windows.Clipboard.SetText(ChiefVerdict);
        }
    }

    [RelayCommand]
    private void CancelCouncil() => _councilCts?.Cancel();

    [RelayCommand]
    private void GoBack()
    {
        _councilCts?.Cancel();
        _navigation.GoBack();
    }

    [RelayCommand]
    private void Done() => _navigation.NavigateHome();
}
