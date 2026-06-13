using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Navigation;

namespace PatchGuard.ViewModels;

public partial class GuideViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;
    private readonly ScanSessionState _session;
    private readonly IAiCouncilService _aiCouncil;

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
    public ObservableCollection<ScanMetric> ScanMetrics { get; } = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _chiefVerdict = string.Empty;

    [ObservableProperty]
    private string _councilStatus = string.Empty;

    [ObservableProperty]
    private bool _isCouncilRunning;

    [ObservableProperty]
    private int _healthScore;

    [ObservableProperty]
    private string? _errorMessage;

    public void OnNavigatedTo() => _ = RunCouncilAsync();

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

        try
        {
            var progress = new Progress<CouncilProgressUpdate>(HandleProgress);

            var guide = await _aiCouncil.BuildGuideAsync(
                scenario,
                _session.Findings,
                progress);

            _session.Guide = guide;
            ApplyGuide(guide);
            MarkAllPhasesComplete();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Council failed: {ex.Message}";
        }
        finally
        {
            IsCouncilRunning = false;
            CouncilStatus = string.Empty;
            foreach (var panel in AgentPanels)
            {
                panel.IsActive = false;
            }
        }
    }

    private void ResetUi()
    {
        CouncilMessages.Clear();
        FixSteps.Clear();
        ScanMetrics.Clear();
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
    }

    [RelayCommand]
    private void OpenLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        // The URL comes from AI/web output, so only open well-formed http(s)
        // links — never arbitrary schemes (file:, javascript:, custom handlers).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ErrorMessage = "Blocked a link that was not a standard http/https web address.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
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
    private void Done() => _navigation.NavigateHome();
}
