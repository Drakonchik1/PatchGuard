using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using PatchGuard.Models;
using PatchGuard.Services.Ai.Tools;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Conditional council graph (LangGraph analog): Analyze → optional ToolResearch → Debate/Rebuttal → Verdict.
/// Light path skips debate when there are no Warning/Critical findings.
/// After the chief verdict, <see cref="FixStepVerifier"/> rejects unsafe steps with at most one retry.
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
        var totalSw = Stopwatch.StartNew();
        var nodesVisited = new List<string>();
        var timings = new List<CouncilTraceNodeTiming>();
        var messages = new List<CouncilMessage>();
        var transcript = new List<(string Role, string Content)>();
        var webBlock = FormatWebResults(webResults);
        var kbBlock = KnowledgeRetrievalService.FormatHits(knowledgeHits);
        var usedToolPath = NeedsToolResearch(findings);
        var toolBlock = usedToolPath
            ? "(tools pending — will run after analysis)"
            : "(tools not invoked — light path)";
        IReadOnlyList<string> toolsInvoked = [];

        await TimedNodeAsync("Analyze", nodesVisited, timings, async () =>
        {
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
        });

        if (usedToolPath)
        {
            await TimedNodeAsync("ToolResearch", nodesVisited, timings, async () =>
            {
                reporter.SetPhase(CouncilPhaseType.Research, "Invoking read-only council tools…");
                var toolResult = await InvokeReadOnlyToolsAsync(findings, cancellationToken);
                toolBlock = toolResult.Block;
                toolsInvoked = toolResult.InvokedNames;
            });

            await TimedNodeAsync("Research", nodesVisited, timings, async () =>
            {
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
            });

            await TimedNodeAsync("Debate", nodesVisited, timings, async () =>
            {
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
            });

            await TimedNodeAsync("Rebuttal", nodesVisited, timings, async () =>
            {
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
            });
        }

        reporter.SetPhase(CouncilPhaseType.Verdict, "Chief Councilor deciding…");
        reporter.DeactivateAgents();

        var debateText = FormatTranscript(messages);
        string chiefRaw = string.Empty;
        var verifyRetryCount = 0;
        IReadOnlyList<string> rejectedReasons = [];

        await TimedNodeAsync("ExplainVerdict", nodesVisited, timings, async () =>
        {
            chiefRaw = await CompleteChiefAsync(
                chat,
                scenario,
                context,
                debateText,
                kbBlock,
                toolBlock,
                webBlock,
                rejectionFeedback: null,
                cancellationToken);

            var verification = FixStepVerifier.VerifyChiefJson(chiefRaw);
            if (!verification.IsValid)
            {
                verifyRetryCount = 1;
                rejectedReasons = verification.RejectionReasons;
                nodesVisited.Add("VerifySteps");
                reporter.SetPhase(CouncilPhaseType.Verdict, "Verifying steps — retrying unsafe plan…");

                chiefRaw = await CompleteChiefAsync(
                    chat,
                    scenario,
                    context,
                    debateText,
                    kbBlock,
                    toolBlock,
                    webBlock,
                    rejectionFeedback: verification.RejectionReasons,
                    cancellationToken);

                var second = FixStepVerifier.VerifyChiefJson(chiefRaw);
                if (!second.IsValid)
                {
                    rejectedReasons = second.RejectionReasons;
                    chiefRaw = StripUnsafeSteps(chiefRaw);
                }
                else
                {
                    rejectedReasons = [];
                }
            }
        });

        totalSw.Stop();
        var trace = new CouncilTrace
        {
            NodesVisited = nodesVisited,
            ToolsCalled = toolsInvoked,
            NodeTimings = timings,
            VerifyRetryCount = verifyRetryCount,
            RejectedStepReasons = rejectedReasons,
            TotalDurationMs = totalSw.ElapsedMilliseconds
        };

        return new CouncilGraphResult
        {
            Messages = messages,
            ChiefRaw = chiefRaw,
            ToolContextBlock = toolBlock,
            UsedToolPath = usedToolPath,
            ToolsInvoked = toolsInvoked,
            Trace = trace
        };
    }

    private static async Task TimedNodeAsync(
        string node,
        List<string> nodesVisited,
        List<CouncilTraceNodeTiming> timings,
        Func<Task> action)
    {
        nodesVisited.Add(node);
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        timings.Add(new CouncilTraceNodeTiming { Node = node, DurationMs = sw.ElapsedMilliseconds });
    }

    private static Task<string> CompleteChiefAsync(
        IChatCompletionProvider chat,
        ScanScenario scenario,
        string context,
        string debateText,
        string kbBlock,
        string toolBlock,
        string webBlock,
        IReadOnlyList<string>? rejectionFeedback,
        CancellationToken cancellationToken)
    {
        var retryBlock = rejectionFeedback is { Count: > 0 }
            ? $"""

            VERIFY FAILED — rewrite JSON without these unsafe steps:
            {string.Join("\n", rejectionFeedback.Select(r => "- " + r))}
            Forbidden: DISM, SFC, registry edits, sc/net start/stop, elevated PowerShell.
            Prefer Settings UI paths and ms-settings: links only.
            """
            : string.Empty;

        return chat.CompleteAsync(
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
            {retryBlock}
            """,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Last-resort filter when the retry still returns unsafe steps: drop offending steps from JSON.
    /// </summary>
    internal static string StripUnsafeSteps(string chiefRaw)
    {
        try
        {
            var json = ExtractJson(chiefRaw);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();
            if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            {
                return chiefRaw;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("steps"))
                    {
                        writer.WritePropertyName("steps");
                        writer.WriteStartArray();
                        foreach (var step in prop.Value.EnumerateArray())
                        {
                            var title = step.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                            var instructions = step.TryGetProperty("instructions", out var i)
                                ? i.GetString() ?? ""
                                : "";
                            var link = step.TryGetProperty("linkUrl", out var l) && l.ValueKind == JsonValueKind.String
                                ? l.GetString()
                                : null;
                            if (!FixStepVerifier.DescribeUnsafe(title, instructions, link).Any())
                            {
                                step.WriteTo(writer);
                            }
                        }

                        writer.WriteEndArray();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            return chiefRaw;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private async Task<(string Block, IReadOnlyList<string> InvokedNames)> InvokeReadOnlyToolsAsync(
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken)
    {
        var query = BuildToolQuery(findings);
        var findingsSummary = CouncilReadOnlyTools.BuildFindingsSummaryJson(findings);
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
                new KernelArguments { ["findingsSummaryJson"] = findingsSummary },
                cancellationToken);
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
    public CouncilTrace? Trace { get; init; }
}
