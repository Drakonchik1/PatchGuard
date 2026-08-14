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
    private NavigationLifecycle? _lifecycle;
    private Task _councilTask = Task.CompletedTask;
    private Task _fixTask = Task.CompletedTask;
    private int _lifecycleGeneration;

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
        _lifecycle = new NavigationLifecycle();
        _lifecycleGeneration = 1;

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
    public ObservableCollection<string> TraceNodes { get; } = [];
    public ObservableCollection<string> TraceTools { get; } = [];
    public ObservableCollection<string> TraceTimings { get; } = [];

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

    [ObservableProperty]
    private bool _hasAgentTrace;

    [ObservableProperty]
    private string _traceSummary = string.Empty;

    [ObservableProperty]
    private bool _isTraceExpanded;

    public void OnNavigatedTo()
    {
        RetireLifecycle();
        _lifecycle = new NavigationLifecycle();
        Interlocked.Increment(ref _lifecycleGeneration);
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
        RetireLifecycle();
        IsCouncilRunning = false;
        IsFixRunning = false;
        RunCouncilCommand.NotifyCanExecuteChanged();
        RunSafeFixCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunCouncil))]
    private Task RunCouncilAsync()
    {
        if (_lifecycle is null)
        {
            return Task.CompletedTask;
        }

        ResetUi();

        if (_session.SelectedScenario is not ScanScenario scenario)
        {
            ErrorMessage = "No scan scenario selected.";
            return Task.CompletedTask;
        }

        LoadScanMetrics();
        _councilCts?.Cancel();
        IsCouncilRunning = true;
        RunCouncilCommand.NotifyCanExecuteChanged();
        if (_lifecycle is not { } lifecycle ||
            !lifecycle.TryAcquire(out var lease))
        {
            IsCouncilRunning = false;
            RunCouncilCommand.NotifyCanExecuteChanged();
            return Task.CompletedTask;
        }

        var generation = Volatile.Read(ref _lifecycleGeneration);
        var councilTask = RunCouncilCoreAsync(
            scenario,
            generation,
            lease!);
        _councilTask = ActiveTaskTracker.Retain(
            _councilTask,
            councilTask);
        return councilTask;
    }

    private async Task RunCouncilCoreAsync(
        ScanScenario scenario,
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        using var councilCts = CancellationTokenSource.CreateLinkedTokenSource(
            lease.CancellationToken);
        _councilCts = councilCts;

        try
        {
            var progress = new Progress<CouncilProgressUpdate>(update =>
            {
                if (IsCurrent(generation, councilCts.Token))
                {
                    HandleProgress(update);
                }
            });

            var guide = await _aiCouncil.BuildGuideAsync(
                scenario,
                _session.Findings,
                progress,
                councilCts.Token,
                HasExternalAiConsent);
            councilCts.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(generation, councilCts.Token))
            {
                return;
            }

            _session.Guide = guide;
            ApplyGuide(guide);
            MarkAllPhasesComplete();
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(generation, CancellationToken.None))
            {
                ErrorMessage = "AI guidance cancelled. No system changes were made.";
            }
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, councilCts.Token))
            {
                ErrorMessage = $"Council failed: {ex.Message}";
            }
        }
        finally
        {
            if (IsCurrent(generation, CancellationToken.None))
            {
                IsCouncilRunning = false;
                HasExternalAiConsent = false;
                RunCouncilCommand.NotifyCanExecuteChanged();
                CouncilStatus = string.Empty;
                foreach (var panel in AgentPanels)
                {
                    panel.IsActive = false;
                }
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
        TraceNodes.Clear();
        TraceTools.Clear();
        TraceTimings.Clear();
        ChiefVerdict = string.Empty;
        DetailedExplanation = string.Empty;
        Summary = string.Empty;
        HealthScore = 0;
        ErrorMessage = null;
        FixStatusMessage = null;
        HasAgentTrace = false;
        TraceSummary = string.Empty;
        IsTraceExpanded = false;

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
                GuidanceSource.AiGenerated => guide.AiProviderName?.ToUpperInvariant() switch
                {
                    "OLLAMA" => "Local LLM (Ollama)",
                    "AZURE" => "Azure OpenAI advice",
                    "OPENAI" => "AI-generated advice",
                    _ => "AI-generated advice"
                },
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

        ApplyTrace(guide.Trace);
    }

    private void ApplyTrace(CouncilTrace? trace)
    {
        TraceNodes.Clear();
        TraceTools.Clear();
        TraceTimings.Clear();

        if (trace is null)
        {
            HasAgentTrace = false;
            TraceSummary = string.Empty;
            return;
        }

        HasAgentTrace = true;
        foreach (var node in trace.NodesVisited)
        {
            TraceNodes.Add(node);
        }

        foreach (var tool in trace.ToolsCalled)
        {
            TraceTools.Add(tool);
        }

        foreach (var timing in trace.NodeTimings)
        {
            TraceTimings.Add($"{timing.Node}: {timing.DurationMs} ms");
        }

        var retry = trace.VerifyRetryCount > 0
            ? $", verify retry ×{trace.VerifyRetryCount}"
            : string.Empty;
        TraceSummary =
            $"{trace.NodesVisited.Count} nodes · {trace.ToolsCalled.Count} tools · {trace.TotalDurationMs} ms{retry}";
    }

    [RelayCommand(CanExecute = nameof(CanRunFix))]
    private Task RunSafeFixAsync(FixStepItemViewModel? item)
    {
        if (item is null ||
            !item.CanRunSafeFix ||
            _lifecycle is null)
        {
            return Task.CompletedTask;
        }

        var scenario = _session.SelectedScenario?.GetTitle();
        var plan = _fixPlans.TryBuildFromFixStep(item.Step, scenario);
        if (plan is null)
        {
            FixStatusMessage = "No safe automated fix is available for this guide step.";
            return Task.CompletedTask;
        }

        var preview = _fixPlans.Preview(plan);
        var confirmMessage =
            $"{preview.Summary}\n\n" +
            string.Join("\n", preview.StepSummaries.Select(s => "• " + s)) +
            "\n\nRun these safe steps now?";

        if (!_confirmation.Confirm("Confirm guided fix", confirmMessage))
        {
            FixStatusMessage = "Fix cancelled. No system changes were made.";
            return Task.CompletedTask;
        }

        IsFixRunning = true;
        RunSafeFixCommand.NotifyCanExecuteChanged();
        FixStatusMessage = "Running safe fix…";
        if (_lifecycle is not { } lifecycle ||
            !lifecycle.TryAcquire(out var lease))
        {
            IsFixRunning = false;
            RunSafeFixCommand.NotifyCanExecuteChanged();
            return Task.CompletedTask;
        }

        var generation = Volatile.Read(ref _lifecycleGeneration);
        var fixTask = RunSafeFixCoreAsync(plan, generation, lease!);
        _fixTask = ActiveTaskTracker.Retain(_fixTask, fixTask);
        return fixTask;
    }

    private async Task RunSafeFixCoreAsync(
        GuidedFixPlan plan,
        int generation,
        NavigationLifecycleLease lease)
    {
        using var lifecycleLease = lease;
        var cancellationToken = lease.CancellationToken;
        try
        {
            var result = await _fixPlans.ExecuteAsync(
                plan,
                GuidedFixConfirmation.ConfirmNow(),
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCurrent(generation, cancellationToken))
            {
                FixStatusMessage = result.Summary;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Navigation lifecycle cancellation is expected.
        }
        catch (Exception ex)
        {
            if (IsCurrent(generation, cancellationToken))
            {
                FixStatusMessage = $"Guided fix failed: {ex.Message}";
            }
        }
        finally
        {
            if (IsCurrent(generation, cancellationToken))
            {
                IsFixRunning = false;
                RunSafeFixCommand.NotifyCanExecuteChanged();
            }
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

    private void RetireLifecycle()
    {
        Interlocked.Increment(ref _lifecycleGeneration);
        Interlocked.Exchange(ref _lifecycle, null)?.Retire();
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && generation == Volatile.Read(ref _lifecycleGeneration);
}
