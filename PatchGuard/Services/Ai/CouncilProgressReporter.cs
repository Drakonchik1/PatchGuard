using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public sealed class CouncilProgressReporter
{
    private readonly IProgress<CouncilProgressUpdate>? _progress;

    public CouncilProgressReporter(IProgress<CouncilProgressUpdate>? progress)
    {
        _progress = progress;
    }

    public void SetPhase(CouncilPhaseType phase, string status)
    {
        _progress?.Report(new CouncilProgressUpdate
        {
            Phase = phase,
            StatusText = status
        });
    }

    public void SetAgentActive(string role, string phaseLabel, CouncilPhaseType phase)
    {
        _progress?.Report(new CouncilProgressUpdate
        {
            Phase = phase,
            ActiveAgent = role,
            Panel = new AgentPanelSnapshot
            {
                Role = role,
                PhaseLabel = phaseLabel,
                Headline = "…",
                Confidence = 0,
                IsActive = true
            }
        });
    }

    public CouncilMessage EmitMessage(CouncilMessage message)
    {
        _progress?.Report(new CouncilProgressUpdate
        {
            Phase = message.Phase,
            ActiveAgent = message.AgentRole,
            Message = message,
            Panel = new AgentPanelSnapshot
            {
                Role = message.AgentRole,
                PhaseLabel = message.Phase.ToString(),
                Headline = message.Headline,
                Confidence = message.Confidence,
                IsActive = true
            }
        });

        return message;
    }

    public void DeactivateAgents()
    {
        foreach (var role in CouncilAgents.Debaters)
        {
            _progress?.Report(new CouncilProgressUpdate
            {
                Panel = new AgentPanelSnapshot
                {
                    Role = role,
                    PhaseLabel = "Done",
                    Headline = "Done",
                    Confidence = 0,
                    IsActive = false
                }
            });
        }
    }

    public void EmitChief(string verdict)
    {
        _progress?.Report(new CouncilProgressUpdate
        {
            Phase = CouncilPhaseType.Verdict,
            ChiefVerdict = verdict,
            StatusText = "Verdict ready."
        });
    }
}
