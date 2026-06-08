using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public sealed class LocalCouncilSession
{
    public async Task<RepairGuide> RunAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var messages = new List<CouncilMessage>();
        var focus = findings.OrderByDescending(f => f.Severity).Take(4).ToList();
        var warnings = findings.Where(f => f.Severity >= FindingSeverity.Warning).ToList();

        async Task DebatePhase(CouncilPhaseType phase, int round, string status, Func<string, string> contentFor)
        {
            reporter.SetPhase(phase, status);
            await Task.Delay(200, cancellationToken);
            foreach (var agent in CouncilAgents.Debaters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reporter.SetAgentActive(agent, phase.ToString(), phase);
                var text = contentFor(agent);
                messages.Add(reporter.EmitMessage(new CouncilMessage
                {
                    AgentRole = agent,
                    Phase = phase,
                    Round = round,
                    Headline = text.Length > 40 ? text[..40] + "…" : text,
                    Confidence = 70,
                    Content = text
                }));
                await Task.Delay(150, cancellationToken);
            }
        }

        await DebatePhase(CouncilPhaseType.Analysis, 1, "Reading scan…", agent => agent switch
        {
            CouncilAgents.Technician => Summarize(focus, "Fix:"),
            CouncilAgents.Skeptic => warnings.Count == 0 ? "Nothing urgent." : $"Confirm {warnings.Count} warning(s) are real.",
            CouncilAgents.Researcher => webResults.Count > 0 ? $"{webResults.Count} web hit(s)." : "Local rules only.",
            _ => ""
        });

        await DebatePhase(CouncilPhaseType.Research, 1, "Checking research…", agent => agent switch
        {
            CouncilAgents.Researcher => focus.Count > 0
                ? LocalKnowledgeBase.GetResearcherOpinion(focus[0], webResults)
                : "No focus item.",
            CouncilAgents.Technician => "Priority unchanged.",
            CouncilAgents.Skeptic => "Skip unsafe tips.",
            _ => ""
        });

        await DebatePhase(CouncilPhaseType.Debate, 1, "Debate…", agent => agent switch
        {
            CouncilAgents.Technician => "Disk/GPU/RAM first.",
            CouncilAgents.Skeptic => "No admin tools.",
            CouncilAgents.Researcher => "Match fixes to scan lines.",
            _ => ""
        });

        await DebatePhase(CouncilPhaseType.Rebuttal, 2, "Final positions…", agent => agent switch
        {
            CouncilAgents.Technician => warnings.Count == 0 ? "Baseline only." : "Do listed steps in order.",
            CouncilAgents.Skeptic => "Agreed.",
            CouncilAgents.Researcher => "Agreed.",
            _ => ""
        });

        reporter.SetPhase(CouncilPhaseType.Verdict, "Chief deciding…");
        reporter.DeactivateAgents();
        await Task.Delay(200, cancellationToken);

        var chief = BuildChief(scenario, warnings, focus);
        var steps = BuildSteps(findings, warnings);
        var health = ComputeHealth(findings);
        var summary = warnings.Count == 0 ? "OK" : $"{warnings.Count} warning(s)";

        reporter.EmitChief(chief);

        return new RepairGuide
        {
            Summary = summary,
            ChiefVerdict = chief,
            HealthScore = health,
            CouncilDiscussion = messages,
            Steps = steps,
            WebReferences = webResults.Take(3).Select(r => new WebReference
            {
                Title = r.Title,
                Url = r.Url,
                UsedFor = "Research"
            }).ToList()
        };
    }

    private static string Summarize(List<Finding> focus, string prefix)
    {
        if (focus.Count == 0)
        {
            return "Clean scan.";
        }

        return prefix + " " + string.Join("; ", focus.Take(2).Select(f => f.Title));
    }

    private static string BuildChief(ScanScenario scenario, List<Finding> warnings, List<Finding> focus)
    {
        if (scenario == ScanScenario.GamePerformanceCheck)
        {
            var render = focus.FirstOrDefault(f => f.ModuleName == "Render test");
            var lines = new List<string>
            {
                "Game FPS check:",
                render is not null ? render.Details : "Run render test.",
                "Log in-game FPS below (same game, same settings) to see if PC changes hurt performance."
            };
            if (warnings.Count > 0)
            {
                lines.Add("Warnings: " + string.Join(", ", warnings.Select(w => w.Title)));
            }

            return string.Join(Environment.NewLine, lines);
        }

        if (warnings.Count == 0)
        {
            return "No warnings. Save OS build and disk free space before the next update.";
        }

        return "Warnings:" + Environment.NewLine +
               string.Join(Environment.NewLine, warnings.Select(w =>
                   $"• {w.Title} — {LocalKnowledgeBase.GetTechnicianOpinion(w)}"));
    }

    private static IReadOnlyList<FixStep> BuildSteps(IReadOnlyList<Finding> findings, List<Finding> warnings)
    {
        var steps = new List<FixStep>();
        var n = 1;

        foreach (var w in warnings.Take(4))
        {
            steps.Add(new FixStep
            {
                Order = n++,
                Title = w.Title,
                Instructions = LocalKnowledgeBase.GetTechnicianOpinion(w),
                LinkUrl = w.Title.Contains("disk", StringComparison.OrdinalIgnoreCase) ? "ms-settings:storagesense" : null
            });
        }

        if (steps.Count == 0)
        {
            steps.Add(new FixStep
            {
                Order = 1,
                Title = "Baseline",
                Instructions = "Note OS build and disk space.",
                LinkUrl = "ms-settings:about"
            });
        }

        return steps;
    }

    private static int ComputeHealth(IReadOnlyList<Finding> findings) =>
        Math.Clamp(100 - findings.Sum(f => f.Severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.Warning => 12,
            _ => 0
        }), 15, 100);
}
