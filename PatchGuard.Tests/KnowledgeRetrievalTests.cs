using System.IO;
using System.Net.Http;
using System.Text;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

public sealed class KnowledgeRetrievalTests
{
    [Fact]
    public void ChunkerSplitsMarkdownByHeadings()
    {
        const string markdown =
            """
            # Disk cleanup
            Intro text.

            ## Safe checks
            Check free space.

            ## Recovery steps
            Empty Recycle Bin.
            """;

        var chunks = KnowledgeChunker.ChunkDocument("disk-cleanup", markdown);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Disk cleanup", chunks[0].Title);
        Assert.Equal("Safe checks", chunks[1].Title);
        Assert.Equal("Recovery steps", chunks[2].Title);
        Assert.All(chunks, chunk => Assert.Equal("disk-cleanup", chunk.PlaybookId));
    }

    [Fact]
    public async Task RetrievalRanksUpdateServicePlaybookForUpdateQuery()
    {
        var service = CreateRetrievalService();

        var hits = await service.RetrieveAsync(
            "Windows 11 Update services troubleshooting",
            topK: 3);

        Assert.NotEmpty(hits);
        Assert.Contains(
            hits,
            hit => hit.Chunk.PlaybookId.Contains("windows-update", StringComparison.OrdinalIgnoreCase)
                   || hit.Chunk.Content.Contains("wuauserv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HybridRetrieval_BoostsExactKeywordPlaybook()
    {
        var service = CreateRetrievalService();
        var hits = await service.RetrieveAsync(
            "wuauserv BITS Background Intelligent Transfer Service restart order",
            topK: 5);

        Assert.NotEmpty(hits);
        var top = hits[0];
        Assert.Contains("windows-update", top.Chunk.PlaybookId, StringComparison.OrdinalIgnoreCase);

        var tokens = KnowledgeRetrievalService.Tokenize("wuauserv BITS restart");
        var keyword = KnowledgeRetrievalService.KeywordOverlap(tokens, top.Chunk);
        Assert.True(keyword > 0, "Expected keyword overlap against the update playbook.");
        Assert.InRange(KnowledgeRetrievalService.HybridScore(0.5, 0.5), 0.49, 0.51);
        Assert.True(top.Score > 0);
    }

    [Fact]
    public void PlaybookCorpus_HasAtLeastFifteenDocuments()
    {
        var playbooks = ResolvePlaybooksDirectory();
        var count = Directory.GetFiles(playbooks, "*.md").Length;
        Assert.True(count >= 15, $"Expected ≥15 playbooks, found {count}.");
    }

    [Fact]
    public async Task CouncilWithoutExternalConsentStillCitesLocalKnowledgeBase()
    {
        var retrieval = CreateRetrievalService();
        var options = new AiOptions();
        var openAi = new OpenAiChatClient(new HttpClient(new RejectingHandler()), options);
        var azure = new AzureOpenAiChatProvider(new HttpClient(new RejectingHandler()), options);
        var ollama = new OllamaChatProvider(new HttpClient(new RejectingHandler()), options);
        var service = CouncilTestFactory.CreateCouncilService(
            new ChatProviderResolver(openAi, azure, ollama, options),
            new DisabledWebSearch(),
            retrieval,
            new HealthScorePolicy(),
            new NoOpEvaluation());

        var guide = await service.BuildGuideAsync(
            ScanScenario.AfterWindowsUpdate,
            [
                new Finding
                {
                    ModuleName = "Update services",
                    Title = "Windows Update service not running",
                    Details = "wuauserv is stopped",
                    Severity = FindingSeverity.Warning,
                    Risk = FindingRisk.Medium
                }
            ],
            allowExternalServices: false);

        Assert.Contains(GuidanceSource.Local, guide.Sources);
        Assert.Contains(GuidanceSource.KnowledgeBase, guide.Sources);
        Assert.DoesNotContain(GuidanceSource.WebSourced, guide.Sources);
        Assert.DoesNotContain(GuidanceSource.AiGenerated, guide.Sources);
        Assert.NotEmpty(guide.KnowledgeReferences);
        Assert.Contains(
            guide.CouncilDiscussion,
            message => message.AgentRole == CouncilAgents.Researcher
                       && message.Content.Contains("KB", StringComparison.OrdinalIgnoreCase));
    }

    private static KnowledgeRetrievalService CreateRetrievalService()
    {
        var playbooks = ResolvePlaybooksDirectory();
        Assert.True(Directory.Exists(playbooks), $"Playbooks missing at {playbooks}");
        return new KnowledgeRetrievalService(new HashingEmbeddingService(), playbooks);
    }

    private static string ResolvePlaybooksDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "KnowledgeBase", "Playbooks"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "PatchGuard", "KnowledgeBase", "Playbooks"))
        };

        return candidates.First(Directory.Exists);
    }

    [Fact]
    public async Task LocalIndexingNeverCallsExternalHttp()
    {
        var handler = new CountingHandler();
        var openAi = new OpenAiEmbeddingService(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") },
            new AiOptions { ApiKey = "sk-should-not-be-used-for-kb", EmbeddingModel = "text-embedding-3-small" },
            new HashingEmbeddingService());

        // KB path must use hashing directly — even if an OpenAI wrapper exists elsewhere.
        var service = new KnowledgeRetrievalService(new HashingEmbeddingService(), ResolvePlaybooksDirectory());
        _ = await service.RetrieveAsync("Windows 11 Update services troubleshooting", topK: 2);

        Assert.Equal(0, handler.Calls);
        Assert.True(openAi.IsConfigured);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class DisabledWebSearch : IWebSearchService
    {
        public bool IsConfigured => false;

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WebSearchResult>>([]);
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("External OpenAI must not be called.");
    }

    private sealed class NoOpEvaluation : ICouncilEvaluationService
    {
        public Task SaveAsync(
            ScanScenario scenario,
            RepairGuide guide,
            TimeSpan latency,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PatchGuard.Data.Entities.CouncilEvaluationRecord>> GetRecentAsync(
            int take = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchGuard.Data.Entities.CouncilEvaluationRecord>>([]);
    }
}
