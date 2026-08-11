using Microsoft.SemanticKernel;
using PatchGuard.Models;
using PatchGuard.Services.Ai.Tools;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Conditional council graph (LangGraph analog): Analyze → optional ToolResearch → Debate/Rebuttal → Verdict.
/// Light path skips debate when there are no Warning/Critical findings.
/// </summary>
public sealed class CouncilAgentGraph
{
    private readonly SemanticKernelToolHost _toolHost;

    public CouncilAgentGraph(SemanticKernelToolHost toolHost)
    {
        _toolHost = toolHost;
    }

    public static bool NeedsToolResearch(IReadOnlyList<Finding> findings) =>
        findings.Any(f => f.Severity >= FindingSeverity.Warning);

    public async Task<CouncilGraphResult> RunAsync(
        IChatCompletionProvider chat,
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        string context,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var messages = new List<CouncilMessage>();
        var transcript = new List<(string Role, string Content)>();
        var webBlock = FormatWebResults(webResults);
        var kbBlock = KnowledgeRetrievalService.FormatHits(knowledgeHits);
        var toolBlock = "(tools not invoked — light path)";
        var usedToolPath = NeedsToolResearch(findings);
        IReadOnlyList<string> toolsInvoked = [];

        await RunDebaterPhaseAsync(
            chat,
            CouncilPhaseType.Analysis,
            "Council analyzing scan…",
            1,
            context,
            kbBlock,
            webBlock,
            toolBlock,
            messages,
            transcript,
            reporter,
            cancellationToken);

        if (usedToolPath)
        {
            reporter.SetPhase(CouncilPhaseType.Research, "Invoking read-only council tools…");
            var toolResult = await InvokeReadOnlyToolsAsync(findings, cancellationToken);
            toolBlock = toolResult.Block;
            toolsInvoked = toolResult.InvokedNames;

            await RunDebaterPhaseAsync(
                chat,
                CouncilPhaseType.Research,
                "Council processing tool research…",
                1,
                context,
                kbBlock,
                webBlock,
                toolBlock,
                messages,
                transcript,
                reporter,
                cancellationToken);

            await RunDebaterPhaseAsync(
                chat,
                CouncilPhaseType.Debate,
                "Debate round 1…",
                1,
                context,
                kbBlock,
                webBlock,
                toolBlock,
                messages,
                transcript,
                reporter,
                cancellationToken);

            await RunDebaterPhaseAsync(
                chat,
                CouncilPhaseType.Rebuttal,
                "Debate round 2 — final positions…",
                2,
                context,
                kbBlock,
                webBlock,
                toolBlock,
                messages,
                transcript,
                reporter,
                cancellationToken);
        }

        reporter.SetPhase(CouncilPhaseType.Verdict, "Chief Councilor deciding…");
        reporter.DeactivateAgents();

        var debateText = FormatTranscript(messages);
        var chiefRaw = await chat.CompleteAsync(
            CouncilAgents.GetSystemPrompt(CouncilAgents.ChiefCouncilor),
            $"""
            Scenario: {scenario.GetTitle()}

            Scan:
            {context}

            Full debate:
            {debateText}

            Local KB:
            {kbBlock}

            Read-only tool results:
            {toolBlock}

            Web:
            {webBlock}
            """,
            cancellationToken: cancellationToken);

        return new CouncilGraphResult
        {
            Messages = messages,
            ChiefRaw = chiefRaw,
            ToolContextBlock = toolBlock,
            UsedToolPath = usedToolPath,
            ToolsInvoked = toolsInvoked
        };
    }

    private async Task<(string Block, IReadOnlyList<string> InvokedNames)> InvokeReadOnlyToolsAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken)
    {
        _toolHost.Tools.SetFindings(findings);
        var query = BuildToolQuery(findings);
        var invoked = new List<string>();

        string kbJson;
        try
        {
            kbJson = await _toolHost.InvokeAsync(
                CouncilReadOnlyTools.QueryKnowledgeBaseName,
                new KernelArguments { ["query"] = query },
                cancellationToken);
            invoked.Add(CouncilReadOnlyTools.QueryKnowledgeBaseName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            kbJson = "{\"hits\":[],\"note\":\"tool failed\"}";
        }

        string statusJson;
        try
        {
            statusJson = await _toolHost.InvokeAsync(
                CouncilReadOnlyTools.GetLocalStatusName,
                cancellationToken: cancellationToken);
            invoked.Add(CouncilReadOnlyTools.GetLocalStatusName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            statusJson = "{\"note\":\"status tool failed\"}";
        }

        var block = $"""
            query_knowledge_base({query}):
            {kbJson}

            get_local_status:
            {statusJson}
            """;

        return (block, invoked);
    }

    private static string BuildToolQuery(IReadOnlyList<Finding> findings)
    {
        var categories = findings
            .Where(f => f.Severity >= FindingSeverity.Warning)
            .Select(f => ExternalDiagnosticSanitizer.SanitizeCategory(f.ModuleName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        if (categories.Count == 0)
        {
            categories = findings
                .Select(f => ExternalDiagnosticSanitizer.SanitizeCategory(f.ModuleName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .DefaultIfEmpty("Windows system health")
                .ToList();
        }

        return $"Windows 11 {string.Join("; ", categories)} troubleshooting";
    }

    private static async Task RunDebaterPhaseAsync(
        IChatCompletionProvider chat,
        CouncilPhaseType phase,
        string status,
        int round,
        string context,
        string kbBlock,
        string webBlock,
        string toolBlock,
        List<CouncilMessage> messages,
        List<(string Role, string Content)> transcript,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        reporter.SetPhase(phase, status);

        foreach (var agent in CouncilAgents.Debaters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.SetAgentActive(agent, phase.ToString(), phase);

            var userPrompt = $"""
                {CouncilAgents.GetPhasePrompt(agent, phase)}

                Scenario context:
                {context}

                Local knowledge base:
                {kbBlock}

                Read-only tool results:
                {toolBlock}

                Web research:
                {webBlock}

                Debate transcript:
                {FormatTranscript(messages)}
                """;

            var reply = await chat.CompleteAsync(
                CouncilAgents.GetSystemPrompt(agent),
                userPrompt,
                transcript,
                cancellationToken);

            var (headline, body) = SplitHeadline(reply);
            var message = new CouncilMessage
            {
                AgentRole = agent,
                Phase = phase,
                Round = round,
                Headline = headline,
                Confidence = 70 + round * 5,
                Content = body
            };

            messages.Add(reporter.EmitMessage(message));
            transcript.Add(("user", userPrompt));
            transcript.Add(("assistant", reply));
            await Task.Delay(150, cancellationToken);
        }
    }

    private static (string Headline, string Body) SplitHeadline(string reply)
    {
        var lines = reply.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return ("Council note", reply);
        }

        var headline = lines[0].Length > 60 ? lines[0][..60] + "…" : lines[0];
        var body = lines.Length > 1 ? string.Join(" ", lines.Skip(1)) : lines[0];
        return (headline, body);
    }

    private static string FormatTranscript(IReadOnlyList<CouncilMessage> messages) =>
        messages.Count == 0
            ? "(no debate yet)"
            : string.Join("\n", messages.Select(m => $"[{m.Phase} R{m.Round} {m.AgentRole}]: {m.Content}"));

    private static string FormatWebResults(IReadOnlyList<WebSearchResult> results) =>
        results.Count == 0
            ? "(no web results — use your own expertise)"
            : string.Join("\n", results.Select(r => $"- {r.Title}: {r.Snippet}"));
}

public sealed class CouncilGraphResult
{
    public required IReadOnlyList<CouncilMessage> Messages { get; init; }
    public required string ChiefRaw { get; init; }
    public required string ToolContextBlock { get; init; }
    public required bool UsedToolPath { get; init; }
    public IReadOnlyList<string> ToolsInvoked { get; init; } = [];
}
