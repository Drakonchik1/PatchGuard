using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class GuideViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IAiCouncilService _aiCouncil;
    private CancellationTokenSource? _councilCts;

    public GuideViewModel(
        INavigationService navigation,
        ScanSessionState session,
        IAiCouncilService aiCouncil)
    {
        _navigation = navigation;
        _session = session;
        _aiCouncil = aiCouncil;

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
    public ObservableCollection<FixStep> FixSteps { get; } = [];
    public ObservableCollection<WebReference> WebReferences { get; } = [];
    public ObservableCollection<ScanMetric> ScanMetrics { get; } = [];
    public ObservableCollection<string> SourceLabels { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _chiefVerdict = string.Empty;

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
        ScanMetrics.Clear();
        SourceLabels.Clear();
        ChiefVerdict = string.Empty;
        Summary = string.Empty;
        HealthScore = 0;
        ErrorMessage = null;

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
        HealthScore = guide.HealthScore;
        SourceLabels.Clear();
        foreach (var source in guide.Sources.Distinct())
        {
            SourceLabels.Add(source switch
            {
                GuidanceSource.Local => "Local diagnostic data",
                GuidanceSource.AiGenerated => "AI-generated advice",
                GuidanceSource.WebSourced => "Web-sourced research",
                _ => "Source unavailable"
            });
        }

        CouncilMessages.Clear();
        foreach (var message in guide.CouncilDiscussion)
        {
            CouncilMessages.Add(message);
        }

        FixSteps.Clear();
        foreach (var step in guide.Steps.OrderBy(s => s.Order))
        {
            FixSteps.Add(step);
        }

        WebReferences.Clear();
        foreach (var reference in guide.WebReferences)
        {
            WebReferences.Add(reference);
        }
    }

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
