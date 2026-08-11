using System.Text;
using PatchGuard.Models;
using PatchGuard.Services.Health;

namespace PatchGuard.Services.Ai;

public sealed class LocalCouncilSession
{
    private readonly IHealthScorePolicy _healthScorePolicy;

    public LocalCouncilSession(IHealthScorePolicy healthScorePolicy)
    {
        _healthScorePolicy = healthScorePolicy;
    }

    public async Task<RepairGuide> RunAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
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
                CouncilAgents.Researcher => BuildResearcherAnalysis(focusFindings, searchBundles, knowledgeHits),
                _ => string.Empty
            };

            var headline = agent switch
            {
                CouncilAgents.Technician => warnings.Count == 0 ? "System baseline OK" : $"{warnings.Count} issue(s) to address",
                CouncilAgents.Skeptic => warnings.Count == 0 ? "No false alarms" : "Validate before acting",
                _ => knowledgeHits.Count > 0 || searchBundles.Sum(b => b.Results.Count) > 0
                    ? "Evidence mapped"
                    : "Playbook mode"
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
                CouncilAgents.Researcher => BuildResearchSynthesis(focusFindings, webResults, searchBundles, knowledgeHits),
                CouncilAgents.Technician => BuildTechnicianResearchReaction(focusFindings, webResults, knowledgeHits),
                CouncilAgents.Skeptic => BuildSkepticResearchReaction(webResults, knowledgeHits),
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
            Content = BuildDebateResearchPosition(focusFindings, webResults, knowledgeHits)
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

        var chiefVerdict = BuildChiefVerdict(scenario, findings, warnings, messages, webResults, knowledgeHits);
        var detailedExplanation = BuildDetailedExplanation(scenario, findings, warnings, knowledgeHits, webResults);
        var actionableWarnings = warnings
            .Where(finding => finding.ActionState == FindingActionState.Recommended)
            .ToList();
        var steps = BuildSteps(findings, actionableWarnings, knowledgeHits);
        var healthScore = _healthScorePolicy.Calculate(findings);
        var summary = healthScore >= 80
            ? "System health good — preventive actions recommended."
            : $"{warnings.Count} warning(s) — follow the Chief's unified plan.";

        reporter.EmitChief(chiefVerdict);

        var references = WebReferenceMapper.FromSearchBundles(searchBundles);
        var kbReferences = KnowledgeRetrievalService.ToReferences(knowledgeHits);
        return new RepairGuide
        {
            Summary = summary,
            ChiefVerdict = chiefVerdict,
            DetailedExplanation = detailedExplanation,
            HealthScore = healthScore,
            CouncilDiscussion = messages,
            Steps = steps,
            WebReferences = references,
            KnowledgeReferences = kbReferences,
            Sources = GuidanceSourceBuilder.Build(
                hasAi: false,
                hasWeb: references.Count > 0,
                hasKnowledgeBase: kbReferences.Count > 0)
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
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> bundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        var sb = new StringBuilder();
        sb.Append(
            $"Mapped {bundles.Sum(b => b.Results.Count)} web hits and {knowledgeHits.Count} local KB excerpts. ");

        if (knowledgeHits.Count > 0)
        {
            sb.AppendLine();
            sb.Append("Local KB: ");
            sb.Append(string.Join(
                "; ",
                knowledgeHits.Take(3).Select(hit =>
                    $"{hit.Chunk.Title} ({hit.Chunk.PlaybookId}, score={hit.Score:F2})")));
            sb.Append('.');
        }

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
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> bundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        var sb = new StringBuilder();
        if (knowledgeHits.Count > 0)
        {
            sb.Append("Local playbooks ranked for this scan: ");
            foreach (var hit in knowledgeHits.Take(3))
            {
                sb.AppendLine();
                sb.Append(
                    $"• KB/{hit.Chunk.PlaybookId} — {hit.Chunk.Title}: {Trim(hit.Chunk.Content, 140)}");
            }

            sb.AppendLine();
        }

        if (allWeb.Count == 0)
        {
            sb.Append(
                knowledgeHits.Count > 0
                    ? "No live web API — grounding steps in the KB excerpts above plus scan-native rules."
                    : "Operating from internal Windows playbooks — no live search API. Patterns still apply: disk space, stopped update services, and noisy DCOM entries after patches.");
            return sb.ToString().Trim();
        }

        sb.Append("Research summary across queries: ");
        foreach (var (query, results) in bundles.Where(b => b.Results.Count > 0))
        {
            sb.AppendLine();
            sb.Append($"• \"{query}\" → {results[0].Title}: {Trim(results[0].Snippet, 100)}");
        }

        sb.AppendLine();
        sb.Append($"Strongest match for \"{focus[0].Title}\": {LocalKnowledgeBase.GetResearcherOpinion(focus[0], allWeb, focus[0].Title)}");
        return sb.ToString().Trim();
    }

    private static string BuildTechnicianResearchReaction(
        List<Finding> focus,
        IReadOnlyList<WebSearchResult> web,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        if (web.Count == 0 && knowledgeHits.Count == 0)
        {
            return "Without web or KB hits I'm still confident in playbook fixes — especially freeing disk and documenting the latest KB before more patching.";
        }

        var source = knowledgeHits.Count > 0 ? "KB playbooks" : "web data";
        return $"{source} reinforce my order: tackle \"{focus.First().Title}\" first, then re-run PatchGuard to confirm the warning cleared.";
    }

    private static string BuildSkepticResearchReaction(
        IReadOnlyList<WebSearchResult> web,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        if (web.Count == 0 && knowledgeHits.Count == 0)
        {
            return "No external sources — double down on scan-native evidence only. Reject any step not tied to a finding we actually saw.";
        }

        return knowledgeHits.Count > 0
            ? "Local KB is safer than random forums, but I still reject any step that needs elevation or third-party cleaners."
            : "External threads often suggest dangerous scripts — I accept only Storage Sense, uninstalls, and Settings-based actions.";
    }

    private static string BuildDebateResearchPosition(
        List<Finding> focus,
        IReadOnlyList<WebSearchResult> web,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        var top = focus.FirstOrDefault();
        if (top is null)
        {
            return "No contested findings — research adds nothing beyond baseline documentation.";
        }

        if (knowledgeHits.Count > 0)
        {
            var hit = knowledgeHits[0];
            return
                $"Weighting local KB + scan: \"{top.Title}\" aligns with playbook \"{hit.Chunk.Title}\" ({hit.Chunk.PlaybookId}). {Trim(hit.Chunk.Content, 160)}";
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
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
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
            sb.AppendLine($"We confirmed {warnings.Count} warning-level item(s). The Technician prioritised concrete fixes; the Skeptic blocked elevated or destructive actions; the Researcher aligned patterns from {(webResults.Count > 0 ? "web threads and local KB" : "local knowledge-base playbooks")}.");
            if (knowledgeHits.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(
                    $"Grounding: local playbook \"{knowledgeHits[0].Chunk.Title}\" ({knowledgeHits[0].Chunk.PlaybookId}) matched this scan.");
            }

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

    private static string BuildDetailedExplanation(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        List<Finding> warnings,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        IReadOnlyList<WebSearchResult> webResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Why this plan for \"{scenario.GetTitle()}\": we ranked {findings.Count} scan signal(s) and focused on {warnings.Count} warning/critical item(s).");
        sb.AppendLine();

        if (warnings.Count == 0)
        {
            sb.AppendLine(
                "No Warning or Critical findings means the council skipped aggressive repair and recommended a baseline capture instead. That keeps you from changing a healthy system without evidence.");
        }
        else
        {
            sb.AppendLine("Each recommended step maps to a Recommended finding from this scan. We order by severity so the highest-impact issue is handled first.");
            foreach (var warning in warnings.Take(3))
            {
                sb.AppendLine($"• {warning.Title} ({warning.Severity}): {Trim(warning.Details, 160)}");
            }
        }

        sb.AppendLine();
        if (knowledgeHits.Count > 0)
        {
            sb.AppendLine(
                $"Local KB support: {string.Join("; ", knowledgeHits.Take(3).Select(h => $"{h.Chunk.Title} [{h.Chunk.PlaybookId}]"))}.");
        }
        else
        {
            sb.AppendLine("Local KB had no strong playbook match; steps stay grounded in scan-native rules only.");
        }

        if (webResults.Count > 0)
        {
            sb.AppendLine($"Web snippets consulted: {webResults.Count} (consent-gated).");
        }

        return sb.ToString().Trim();
    }

    private static IReadOnlyList<FixStep> BuildSteps(
        IReadOnlyList<Finding> findings,
        List<Finding> warnings,
        IReadOnlyList<KnowledgeHit> knowledgeHits)
    {
        var steps = new List<FixStep>();
        var order = 1;
        var kbEvidence = knowledgeHits.Count > 0
            ? $"KB: {knowledgeHits[0].Chunk.Title} ({knowledgeHits[0].Chunk.PlaybookId})"
            : null;

        foreach (var finding in warnings)
        {
            if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase))
            {
                steps.Add(new FixStep
                {
                    Order = order++,
                    Title = "Free disk space",
                    Instructions = "Settings → System → Storage → turn on Storage Sense → run cleanup on temp files. Uninstall 1–2 large unused apps. Empty Recycle Bin.",
                    LinkUrl = "ms-settings:storagesense",
                    WhyThisMatters = "Low free space blocks Windows Update staging and increases update-failure risk.",
                    Evidence = $"Scan: {finding.Title}. {kbEvidence ?? "Rules: disk-space playbook."}"
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
                    CopyText = "services.msc",
                    WhyThisMatters = "Stopped update services prevent patches from downloading or installing.",
                    Evidence = $"Scan: {finding.Title} — {Trim(finding.Details, 120)}. {kbEvidence ?? "Rules: update-services check."}"
                });
                continue;
            }

            steps.Add(new FixStep
            {
                Order = order++,
                Title = finding.Title,
                Instructions = LocalKnowledgeBase.GetTechnicianOpinion(finding),
                LinkUrl = finding.ModuleName == "Windows Update history" ? "ms-settings:windowsupdate" : null,
                WhyThisMatters = $"{finding.Severity} finding in {finding.ModuleName} should be resolved before stacking more changes.",
                Evidence = $"Scan evidence: {Trim(finding.Details, 160)}. {kbEvidence ?? "Rules: technician playbook."}"
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
                LinkUrl = "ms-settings:about",
                WhyThisMatters = "A clean scan still benefits from a dated baseline so the next patch can be compared honestly.",
                Evidence = os is not null
                    ? $"OS signal: {os.Title}."
                    : "No warning findings — preventive baseline only."
            });
        }

        return steps;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
