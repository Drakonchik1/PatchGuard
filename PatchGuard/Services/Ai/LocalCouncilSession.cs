using System.Text;
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
        var focusFindings = SelectFocusFindings(findings);
        var warnings = findings.Where(f => f.Severity >= FindingSeverity.Warning).ToList();

        // Phase 1 — independent analysis
        reporter.SetPhase(CouncilPhaseType.Analysis, "Council reviewing scan data…");
        await Task.Delay(350, cancellationToken);

        foreach (var agent in CouncilAgents.Debaters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.SetAgentActive(agent, "Analyzing", CouncilPhaseType.Analysis);
            var content = agent switch
            {
                CouncilAgents.Technician => BuildTechnicianAnalysis(focusFindings, findings),
                CouncilAgents.Skeptic => BuildSkepticAnalysis(focusFindings, findings),
                CouncilAgents.Researcher => BuildResearcherAnalysis(focusFindings, searchBundles),
                _ => string.Empty
            };

            var headline = agent switch
            {
                CouncilAgents.Technician => warnings.Count == 0 ? "System baseline OK" : $"{warnings.Count} issue(s) to address",
                CouncilAgents.Skeptic => warnings.Count == 0 ? "No false alarms" : "Validate before acting",
                _ => searchBundles.Sum(b => b.Results.Count) > 0 ? "Web data mapped" : "Playbook mode"
            };

            messages.Add(reporter.EmitMessage(new CouncilMessage
            {
                AgentRole = agent,
                Phase = CouncilPhaseType.Analysis,
                Round = 1,
                Headline = headline,
                Confidence = agent == CouncilAgents.Skeptic ? 62 : 74,
                Content = content
            }));
            await Task.Delay(280, cancellationToken);
        }

        // Phase 2 — research synthesis
        reporter.SetPhase(CouncilPhaseType.Research, "Cross-checking fixes from research…");
        await Task.Delay(350, cancellationToken);

        foreach (var agent in CouncilAgents.Debaters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.SetAgentActive(agent, "Researching", CouncilPhaseType.Research);

            var content = agent switch
            {
                CouncilAgents.Researcher => BuildResearchSynthesis(focusFindings, webResults, searchBundles),
                CouncilAgents.Technician => BuildTechnicianResearchReaction(focusFindings, webResults),
                CouncilAgents.Skeptic => BuildSkepticResearchReaction(webResults),
                _ => string.Empty
            };

            messages.Add(reporter.EmitMessage(new CouncilMessage
            {
                AgentRole = agent,
                Phase = CouncilPhaseType.Research,
                Round = 1,
                Headline = agent == CouncilAgents.Researcher ? "Evidence compiled" : "Research reviewed",
                Confidence = 68,
                Content = content
            }));
            await Task.Delay(280, cancellationToken);
        }

        // Phase 3 — debate round 1
        reporter.SetPhase(CouncilPhaseType.Debate, "Debate round 1 — positions clash…");
        await Task.Delay(350, cancellationToken);

        var techAnalysis = messages.Last(m => m.AgentRole == CouncilAgents.Technician && m.Phase == CouncilPhaseType.Analysis);
        var skepticAnalysis = messages.Last(m => m.AgentRole == CouncilAgents.Skeptic && m.Phase == CouncilPhaseType.Analysis);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Technician,
            Phase = CouncilPhaseType.Debate,
            Round = 1,
            Headline = "Defends priority order",
            Confidence = 76,
            Content = $"Skeptic is right to question noise events, but disk/service warnings stay top priority. {Trim(techAnalysis.Content, 120)}"
        }));
        await Task.Delay(250, cancellationToken);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Skeptic,
            Phase = CouncilPhaseType.Debate,
            Round = 1,
            Headline = "Pushes back on panic",
            Confidence = 71,
            Content = $"I'll veto any fix that needs admin or third-party cleaners. {Trim(skepticAnalysis.Content, 120)}"
        }));
        await Task.Delay(250, cancellationToken);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Researcher,
            Phase = CouncilPhaseType.Debate,
            Round = 1,
            Headline = "Sides with evidence",
            Confidence = 73,
            Content = BuildDebateResearchPosition(focusFindings, webResults)
        }));
        await Task.Delay(250, cancellationToken);

        // Phase 4 — rebuttal / convergence
        reporter.SetPhase(CouncilPhaseType.Rebuttal, "Debate round 2 — narrowing the plan…");
        await Task.Delay(350, cancellationToken);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Technician,
            Phase = CouncilPhaseType.Rebuttal,
            Round = 2,
            Headline = "Final technical stance",
            Confidence = 82,
            Content = BuildTechnicianFinal(focusFindings, warnings)
        }));
        await Task.Delay(250, cancellationToken);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Skeptic,
            Phase = CouncilPhaseType.Rebuttal,
            Round = 2,
            Headline = "Accepts safe plan",
            Confidence = 78,
            Content = warnings.Count == 0
                ? "No objections — preventive baseline only. Do not install optional feature updates the same day as cumulative patches."
                : "I accept the manual plan if we skip registry tweakers and service restarts without admin."
        }));
        await Task.Delay(250, cancellationToken);

        messages.Add(reporter.EmitMessage(new CouncilMessage
        {
            AgentRole = CouncilAgents.Researcher,
            Phase = CouncilPhaseType.Rebuttal,
            Round = 2,
            Headline = "Consensus evidence",
            Confidence = 80,
            Content = "Thread patterns and our scan align: fix space and service blockers first, ignore benign DCOM chatter unless apps crash."
        }));

        // Phase 5 — chief
        reporter.SetPhase(CouncilPhaseType.Verdict, "Chief Councilor synthesizing…");
        await Task.Delay(400, cancellationToken);
        reporter.DeactivateAgents();

        var chiefVerdict = BuildChiefVerdict(scenario, findings, warnings, messages, webResults);
        var steps = BuildSteps(findings, warnings);
        var healthScore = ComputeHealthScore(findings);
        var summary = healthScore >= 80
            ? "System health good — preventive actions recommended."
            : $"{warnings.Count} warning(s) — follow the Chief's unified plan.";

        reporter.EmitChief(chiefVerdict);

        return new RepairGuide
        {
            Summary = summary,
            ChiefVerdict = chiefVerdict,
            HealthScore = healthScore,
            CouncilDiscussion = messages,
            Steps = steps,
            WebReferences = webResults.Select(r => new WebReference
            {
                Title = r.Title,
                Url = r.Url,
                UsedFor = "Council research"
            }).ToList()
        };
    }

    private static List<Finding> SelectFocusFindings(IReadOnlyList<Finding> findings) =>
        findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ModuleName)
            .Take(5)
            .ToList();

    private static string BuildTechnicianAnalysis(List<Finding> focus, IReadOnlyList<Finding> all)
    {
        var sb = new StringBuilder();
        sb.Append($"Reviewed {all.Count} signals. ");
        foreach (var f in focus)
        {
            sb.AppendLine();
            sb.Append($"• {f.Title}: {LocalKnowledgeBase.GetTechnicianOpinion(f)}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildSkepticAnalysis(List<Finding> focus, IReadOnlyList<Finding> all)
    {
        var sb = new StringBuilder();
        sb.Append($"Sanity-checking {all.Count} items. ");
        foreach (var f in focus)
        {
            var tech = LocalKnowledgeBase.GetTechnicianOpinion(f);
            sb.AppendLine();
            sb.Append($"• {f.Title}: {LocalKnowledgeBase.GetSkepticOpinion(f, tech)}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildResearcherAnalysis(
        List<Finding> focus,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> bundles)
    {
        var sb = new StringBuilder();
        sb.Append($"Mapped {bundles.Sum(b => b.Results.Count)} web hits to findings. ");
        foreach (var f in focus)
        {
            var bundle = bundles.FirstOrDefault(b =>
                b.Query.Contains(f.Title, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(bundle.Query) && bundles.Count > 0)
            {
                bundle = bundles[0];
            }

            var query = string.IsNullOrEmpty(bundle.Query) ? f.Title : bundle.Query;
            var results = bundle.Results ?? [];
            sb.AppendLine();
            sb.Append($"• {f.Title}: {LocalKnowledgeBase.GetResearcherOpinion(f, results, query)}");
        }

        return sb.ToString().Trim();
    }

    private static string BuildResearchSynthesis(
        List<Finding> focus,
        IReadOnlyList<WebSearchResult> allWeb,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> bundles)
    {
        if (allWeb.Count == 0)
        {
            return "Operating from internal Windows playbooks — no live search API. Patterns still apply: disk space, stopped update services, and noisy DCOM entries after patches.";
        }

        var sb = new StringBuilder("Research summary across queries: ");
        foreach (var (query, results) in bundles.Where(b => b.Results.Count > 0))
        {
            sb.AppendLine();
            sb.Append($"• \"{query}\" → {results[0].Title}: {Trim(results[0].Snippet, 100)}");
        }

        sb.AppendLine();
        sb.Append($"Strongest match for \"{focus[0].Title}\": {LocalKnowledgeBase.GetResearcherOpinion(focus[0], allWeb, focus[0].Title)}");
        return sb.ToString().Trim();
    }

    private static string BuildTechnicianResearchReaction(List<Finding> focus, IReadOnlyList<WebSearchResult> web)
    {
        if (web.Count == 0)
        {
            return "Without web hits I'm still confident in playbook fixes — especially freeing disk and documenting the latest KB before more patching.";
        }

        return $"Web data reinforces my order: tackle \"{focus.First().Title}\" first, then re-run PatchGuard to confirm the warning cleared.";
    }

    private static string BuildSkepticResearchReaction(IReadOnlyList<WebSearchResult> web)
    {
        return web.Count == 0
            ? "No external sources — double down on scan-native evidence only. Reject any step not tied to a finding we actually saw."
            : "External threads often suggest dangerous scripts — I accept only Storage Sense, uninstalls, and Settings-based actions.";
    }

    private static string BuildDebateResearchPosition(List<Finding> focus, IReadOnlyList<WebSearchResult> web)
    {
        var top = focus.FirstOrDefault();
        if (top is null)
        {
            return "No contested findings — research adds nothing beyond baseline documentation.";
        }

        return $"Weighting community data + scan: \"{top.Title}\" is the anchor issue. {LocalKnowledgeBase.GetResearcherOpinion(top, web, top.Title)}";
    }

    private static string BuildTechnicianFinal(List<Finding> focus, List<Finding> warnings)
    {
        if (warnings.Count == 0)
        {
            return "Final stance: capture build + disk metrics today. After the next update, rescan within 24h — if only DCOM noise appears, ignore it.";
        }

        return $"Final stance: execute manual fixes for {string.Join(", ", warnings.Take(3).Select(w => w.Title))} before installing further patches.";
    }

    private static string BuildChiefVerdict(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        List<Finding> warnings,
        IReadOnlyList<CouncilMessage> debate,
        IReadOnlyList<WebSearchResult> webResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Chief decision for \"{scenario.GetTitle()}\" after two debate rounds.");
        sb.AppendLine();

        if (warnings.Count == 0)
        {
            sb.AppendLine("The council agrees your machine shows no actionable warnings in this scan. That is not a promise future updates will be flawless — it means we found no disk, service, or critical log pattern that demands immediate manual work.");
            sb.AppendLine();
            sb.AppendLine("My order: (1) Open Settings → System → About and save the build number. (2) Note free space on C:. (3) Install the next Windows update on a day you can reboot twice. (4) Run PatchGuard again within 24 hours after patching — compare findings side by side.");
        }
        else
        {
            sb.AppendLine($"We confirmed {warnings.Count} warning-level item(s). The Technician prioritised concrete fixes; the Skeptic blocked elevated or destructive actions; the Researcher aligned patterns from {(webResults.Count > 0 ? "web threads" : "internal playbooks")}.");
            sb.AppendLine();
            sb.AppendLine("Unified plan:");
            var step = 1;
            foreach (var w in warnings.Take(4))
            {
                sb.AppendLine($"{step}. {w.Title} — {LocalKnowledgeBase.GetTechnicianOpinion(w)}");
                step++;
            }

            sb.AppendLine();
            sb.AppendLine("Do not stack optional driver updates the same day. Re-scan after each change so we know which action actually moved the needle.");
        }

        return sb.ToString().Trim();
    }

    private static IReadOnlyList<FixStep> BuildSteps(IReadOnlyList<Finding> findings, List<Finding> warnings)
    {
        var steps = new List<FixStep>();
        var order = 1;

        foreach (var finding in warnings)
        {
            if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new FixStep
                {
                    Order = order++,
                    Title = "Free disk space",
                    Instructions = "Settings → System → Storage → turn on Storage Sense → run cleanup on temp files. Uninstall 1–2 large unused apps. Empty Recycle Bin.",
                    LinkUrl = "ms-settings:storagesense"
                });
                continue;
            }

            if (finding.ModuleName == "Update services" && finding.Details.Contains("not running", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new FixStep
                {
                    Order = order++,
                    Title = "Inspect update services",
                    Instructions = "Press Win+R, type services.msc, find Windows Update and BITS. If stopped and Start is greyed out, you need an admin account — note status for IT.",
                    CopyText = "services.msc"
                });
                continue;
            }

            steps.Add(new FixStep
            {
                Order = order++,
                Title = finding.Title,
                Instructions = LocalKnowledgeBase.GetTechnicianOpinion(finding),
                LinkUrl = finding.ModuleName == "Windows Update history" ? "ms-settings:windowsupdate" : null
            });
        }

        if (steps.Count == 0)
        {
            var os = findings.FirstOrDefault(f => f.ModuleName == "Operating system");
            steps.Add(new FixStep
            {
                Order = 1,
                Title = "Save baseline",
                Instructions = os is not null
                    ? $"Record: {os.Title}. Compare after the next patch."
                    : "Record build and disk space from Settings → About.",
                LinkUrl = "ms-settings:about"
            });
        }

        return steps;
    }

    private static int ComputeHealthScore(IReadOnlyList<Finding> findings)
    {
        var penalty = findings.Sum(f => f.Severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.Warning => 12,
            _ => 0
        });

        return Math.Clamp(100 - penalty, 15, 100);
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
