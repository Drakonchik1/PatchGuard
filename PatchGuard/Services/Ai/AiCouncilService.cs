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

    private readonly ChatProviderResolver _chatResolver;
    private readonly IWebSearchService _webSearch;
    private readonly IKnowledgeRetrievalService _knowledge;
    private readonly IHealthScorePolicy _healthScorePolicy;
    private readonly ICouncilEvaluationService _evaluationService;
    private readonly CouncilAgentGraph _agentGraph;
    private readonly LocalCouncilSession _localSession;

    public AiCouncilService(
        ChatProviderResolver chatResolver,
        IWebSearchService webSearch,
        IKnowledgeRetrievalService knowledge,
        IHealthScorePolicy healthScorePolicy,
        ICouncilEvaluationService evaluationService,
        CouncilAgentGraph agentGraph)
    {
        _chatResolver = chatResolver;
        _webSearch = webSearch;
        _knowledge = knowledge;
        _healthScorePolicy = healthScorePolicy;
        _evaluationService = evaluationService;
        _agentGraph = agentGraph;
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

        var chat = _chatResolver.Resolve(allowExternalServices);
        var useWeb = allowExternalServices && _webSearch.IsConfigured;

        RepairGuide guide;
        if (chat is null && !useWeb)
        {
            guide = await _localSession.RunAsync(
                scenario, findings, [], [], knowledgeHits, reporter, cancellationToken);
        }
        else if (chat is null)
        {
            var searchBundles = await RunSearchesAsync(findings, reporter, cancellationToken);
            var allWeb = searchBundles.SelectMany(b => b.Results).DistinctBy(r => r.Url).ToList();
            guide = await _localSession.RunAsync(
                scenario, findings, allWeb, searchBundles, knowledgeHits, reporter, cancellationToken);
        }
        else
        {
            var context = ExternalDiagnosticSanitizer.BuildContext(scenario, findings);
            IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles = [];
            IReadOnlyList<WebSearchResult> allWeb = [];
            if (useWeb)
            {
                searchBundles = await RunSearchesAsync(findings, reporter, cancellationToken);
                allWeb = searchBundles.SelectMany(b => b.Results).DistinctBy(r => r.Url).ToList();
            }

            try
            {
                guide = await RunLlmCouncilAsync(
                    chat,
                    scenario,
                    findings,
                    context,
                    allWeb,
                    searchBundles,
                    knowledgeHits,
                    reporter,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Local LLM / cloud failures fall back to deterministic rules.
                guide = await _localSession.RunAsync(
                    scenario, findings, allWeb, searchBundles, knowledgeHits, reporter, cancellationToken);
            }
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

    private async Task<RepairGuide> RunLlmCouncilAsync(
        IChatCompletionProvider chat,
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        string context,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        CouncilProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var graphResult = await _agentGraph.RunAsync(
            chat,
            scenario,
            findings,
            context,
            webResults,
            knowledgeHits,
            reporter,
            cancellationToken);

        var guide = await ParseChiefResponseAsync(
            graphResult.ChiefRaw,
            chat.Name,
            scenario,
            findings,
            graphResult.Messages,
            webResults,
            searchBundles,
            knowledgeHits,
            graphResult.Trace,
            cancellationToken);
        reporter.EmitChief(guide.ChiefVerdict);
        return guide;
    }

    private async Task<RepairGuide> ParseChiefResponseAsync(
        string chiefRaw,
        string aiProviderName,
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        IReadOnlyList<CouncilMessage> debate,
        IReadOnlyList<WebSearchResult> webResults,
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> searchBundles,
        IReadOnlyList<KnowledgeHit> knowledgeHits,
        CouncilTrace? trace,
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
                        WhyThisMatters = NullIfWhiteSpace(s.Why),
                        Evidence = NullIfWhiteSpace(s.Evidence),
                        LinkUrl = NormalizeStepLink(s.LinkUrl),
                        CopyText = s.CopyText
                    })
                    .Where(FixStepVerifier.IsSafe)
                    .Select((s, i) => new FixStep
                    {
                        Order = i + 1,
                        Title = s.Title,
                        Instructions = s.Instructions,
                        WhyThisMatters = s.WhyThisMatters,
                        Evidence = s.Evidence,
                        LinkUrl = s.LinkUrl,
                        CopyText = s.CopyText
                    })
                    .ToList() ?? [];

                var references = WebReferenceMapper.FromSearchBundles(searchBundles);
                return new RepairGuide
                {
                    Summary = parsed.Summary ?? "Council decision ready.",
                    ChiefVerdict = parsed.Verdict,
                    DetailedExplanation = NullIfWhiteSpace(parsed.DetailedExplanation),
                    HealthScore = _healthScorePolicy.Calculate(findings),
                    CouncilDiscussion = debate,
                    Steps = steps,
                    WebReferences = references,
                    KnowledgeReferences = kbReferences,
                    AiProviderName = aiProviderName,
                    Trace = trace,
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
            DetailedExplanation = local.DetailedExplanation,
            HealthScore = local.HealthScore,
            CouncilDiscussion = debate,
            Steps = local.Steps,
            WebReferences = local.WebReferences,
            KnowledgeReferences = local.KnowledgeReferences,
            AiProviderName = aiProviderName,
            Trace = trace,
            Sources = GuidanceSourceBuilder.Build(
                hasAi: true,
                hasWeb: local.WebReferences.Count > 0,
                hasKnowledgeBase: local.KnowledgeReferences.Count > 0)
        };
    }

    private static string? NormalizeStepLink(string? linkUrl)
    {
        if (LaunchUriPolicy.TryNormalize(linkUrl, out var launchUri) && launchUri is not null)
        {
            return launchUri;
        }

        return null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private sealed class ChiefResponseDto
    {
        public string? Summary { get; set; }
        public string? Verdict { get; set; }
        public string? DetailedExplanation { get; set; }
        public List<ChiefStepDto>? Steps { get; set; }
    }

    private sealed class ChiefStepDto
    {
        public string? Title { get; set; }
        public string? Instructions { get; set; }
        public string? Why { get; set; }
        public string? Evidence { get; set; }
        public string? LinkUrl { get; set; }
        public string? CopyText { get; set; }
    }
}
