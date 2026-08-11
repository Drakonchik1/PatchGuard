using System.Diagnostics;
using System.Text.Json;
using PatchGuard.Models;
using PatchGuard.Services.Health;

namespace PatchGuard.Services.Ai;

public sealed class AiCouncilService : IAiCouncilService
{
    private static readonly JsonSerializerOptions ChiefJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OpenAiChatClient _openAi;
    private readonly IWebSearchService _webSearch;
    private readonly IKnowledgeRetrievalService _knowledge;
    private readonly IHealthScorePolicy _healthScorePolicy;
    private readonly ICouncilEvaluationService _evaluationService;
    private readonly LocalCouncilSession _localSession;

    public AiCouncilService(
        OpenAiChatClient openAi,
        IWebSearchService webSearch,
        IKnowledgeRetrievalService knowledge,
        IHealthScorePolicy healthScorePolicy,
        ICouncilEvaluationService evaluationService)
    {
        _openAi = openAi;
        _webSearch = webSearch;
        _knowledge = knowledge;
        _healthScorePolicy = healthScorePolicy;
        _evaluationService = evaluationService;
        _localSession = new LocalCouncilSession(healthScorePolicy);
    }

    public async Task<RepairGuide> BuildGuideAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IProgress<CouncilProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default,
        bool allowExternalServices = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var reporter = new CouncilProgressReporter(progress);

        // Local KB retrieval does not leave the machine — no external consent required.
        reporter.SetPhase(CouncilPhaseType.Research, "Retrieving local playbooks…");
        IReadOnlyList<KnowledgeHit> knowledgeHits = [];
        try
        {
            knowledgeHits = await _knowledge.RetrieveForFindingsAsync(
                findings,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Retrieval is best-effort; guidance must still run from scan-native rules.
            knowledgeHits = [];
        }

        RepairGuide guide;
        if (!allowExternalServices || (!_openAi.IsConfigured && !_webSearch.IsConfigured))
        {
            guide = await _localSession.RunAsync(
                scenario, findings, [], [], knowledgeHits, reporter, cancellationToken);
        }
        else
        {
            var context = ExternalDiagnosticSanitizer.BuildContext(scenario, findings);
            var searchBundles = await RunSearchesAsync(findings, reporter, cancellationToken);
            var allWeb = searchBundles.SelectMany(b => b.Results).DistinctBy(r => r.Url).ToList();

            guide = !_openAi.IsConfigured
                ? await _localSession.RunAsync(
                    scenario, findings, allWeb, searchBundles, knowledgeHits, reporter, cancellationToken)
                : await RunOpenAiCouncilAsync(
                    scenario,
                    findings,
                    context,
                    allWeb,
                    searchBundles,
                    knowledgeHits,
                    reporter,
                    cancellationToken);
        }

        await SaveEvaluationAsync(scenario, guide, stopwatch.Elapsed, cancellationToken);
        return guide;
    }

    private async Task SaveEvaluationAsync(
        ScanScenario scenario,
        RepairGuide guide,
        TimeSpan latency,
        CancellationToken cancellationToken)
    {
        try
        {
            await _evaluationService.SaveAsync(scenario, guide, latency, cancellationToken);
        }
        catch
        {
            // Evaluation persistence is best-effort and must not block guidance.
        }
    }

    private async Task<List<(string Query, IReadOnlyList<WebSearchResult> Results)>> RunSearchesAsync(
        IReadOnlyList<Finding> findings,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        reporter.SetPhase(CouncilPhaseType.Research, "Searching for known fixes…");

        var bundles = new List<(string, IReadOnlyList<WebSearchResult>)>();
        foreach (var query in ExternalDiagnosticSanitizer.BuildSearchQueries(findings))
        {
            cancellationToken.ThrowIfCancellationRequested();
            reporter.SetPhase(CouncilPhaseType.Research, $"Searching: {Trim(query, 50)}…");
            var results = (await _webSearch.SearchAsync(query, cancellationToken))
                .Where(result => ExternalUrlPolicy.TryNormalize(result.Url, out _))
                .ToList();
            bundles.Add((query, results));
            await Task.Delay(200, cancellationToken);
        }

        return bundles;
    }

    private async Task<RepairGuide> RunOpenAiCouncilAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        string context,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var messages = new List<CouncilMessage>();
        var transcript = new List<(string Role, string Content)>();
        var webBlock = FormatWebResults(webResults);
        var kbBlock = KnowledgeRetrievalService.FormatHits(knowledgeHits);

        foreach (var phase in new[]
                 {
                     CouncilPhaseType.Analysis,
                     CouncilPhaseType.Research,
                     CouncilPhaseType.Debate,
                     CouncilPhaseType.Rebuttal
                 })
        {
            reporter.SetPhase(phase, phase switch
            {
                CouncilPhaseType.Analysis => "Council analyzing scan…",
                CouncilPhaseType.Research => "Council processing research…",
                CouncilPhaseType.Debate => "Debate round 1…",
                CouncilPhaseType.Rebuttal => "Debate round 2 — final positions…",
                _ => "Council working…"
            });

            var round = phase is CouncilPhaseType.Debate or CouncilPhaseType.Rebuttal
                ? (phase == CouncilPhaseType.Debate ? 1 : 2)
                : 1;

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

                    Web research:
                    {webBlock}

                    Debate transcript:
                    {FormatTranscript(messages)}
                    """;

                var reply = await _openAi.CompleteAsync(
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

        reporter.SetPhase(CouncilPhaseType.Verdict, "Chief Councilor deciding…");
        reporter.DeactivateAgents();

        var debateText = FormatTranscript(messages);
        var chiefRaw = await _openAi.CompleteAsync(
            CouncilAgents.GetSystemPrompt(CouncilAgents.ChiefCouncilor),
            $"Scenario: {scenario.GetTitle()}\n\nScan:\n{context}\n\nFull debate:\n{debateText}\n\nLocal KB:\n{kbBlock}\n\nWeb:\n{webBlock}",
            cancellationToken: cancellationToken);

        var guide = await ParseChiefResponseAsync(
            chiefRaw,
            scenario,
            findings,
            messages,
            webResults,
            searchBundles,
            knowledgeHits,
            cancellationToken);
        reporter.EmitChief(guide.ChiefVerdict);
        return guide;
    }

    private async Task<RepairGuide> ParseChiefResponseAsync(
        string chiefRaw,
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<CouncilMessage> debate,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kbReferences = KnowledgeRetrievalService.ToReferences(knowledgeHits);

        try
        {
            var json = ExtractJson(chiefRaw);
            var parsed = JsonSerializer.Deserialize<ChiefResponseDto>(json, ChiefJsonOptions);
            if (parsed?.Verdict is not null)
            {
                var steps = parsed.Steps?
                    .Select((s, i) => new FixStep
                    {
                        Order = i + 1,
                        Title = s.Title ?? $"Step {i + 1}",
                        Instructions = s.Instructions ?? string.Empty,
                        LinkUrl = ExternalUrlPolicy.TryNormalize(s.LinkUrl, out var link)
                            ? link!.AbsoluteUri
                            : null,
                        CopyText = s.CopyText
                    })
                    .ToList() ?? [];

                var references = WebReferenceMapper.FromSearchBundles(searchBundles);
                return new RepairGuide
                {
                    Summary = parsed.Summary ?? "Council decision ready.",
                    ChiefVerdict = parsed.Verdict,
                    HealthScore = _healthScorePolicy.Calculate(findings),
                    CouncilDiscussion = debate,
                    Steps = steps,
                    WebReferences = references,
                    KnowledgeReferences = kbReferences,
                    Sources = GuidanceSourceBuilder.Build(
                        hasAi: true,
                        hasWeb: references.Count > 0,
                        hasKnowledgeBase: kbReferences.Count > 0)
                };
            }
        }
        catch
        {
            // fallback below
        }

        var reporter = new CouncilProgressReporter(null);
        var local = await _localSession.RunAsync(
            scenario,
            findings,
            webResults,
            searchBundles,
            knowledgeHits,
            reporter,
            cancellationToken);

        return new RepairGuide
        {
            Summary = local.Summary,
            ChiefVerdict = local.ChiefVerdict,
            HealthScore = local.HealthScore,
            CouncilDiscussion = debate,
            Steps = local.Steps,
            WebReferences = local.WebReferences,
            KnowledgeReferences = local.KnowledgeReferences,
            Sources = GuidanceSourceBuilder.Build(
                hasAi: true,
                hasWeb: local.WebReferences.Count > 0,
                hasKnowledgeBase: local.KnowledgeReferences.Count > 0)
        };
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

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static string FormatWebResults(IReadOnlyList<WebSearchResult> results) =>
        results.Count == 0
            ? "(no web results — use your own expertise)"
            : string.Join("\n", results.Select(r => $"- {r.Title}: {r.Snippet}"));

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private sealed class ChiefResponseDto
    {
        public string? Summary { get; set; }
        public string? Verdict { get; set; }
        public List<ChiefStepDto>? Steps { get; set; }
    }

    private sealed class ChiefStepDto
    {
        public string? Title { get; set; }
        public string? Instructions { get; set; }
        public string? LinkUrl { get; set; }
        public string? CopyText { get; set; }
    }
}
