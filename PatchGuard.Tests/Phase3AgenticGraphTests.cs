using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.SemanticKernel;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Ai.Tools;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

public sealed class Phase3AgenticGraphTests
{
    [Fact]
    public void NeedsToolResearch_TrueOnlyForWarningOrCritical()
    {
        Assert.False(CouncilAgentGraph.NeedsToolResearch(
        [
            InfoFinding("OS build")
        ]));

        Assert.True(CouncilAgentGraph.NeedsToolResearch(
        [
            InfoFinding("OS build"),
            WarningFinding("Low disk space")
        ]));
    }

    [Fact]
    public async Task LightPath_SkipsToolResearchAndDebatePhases()
    {
        var chat = new ScriptedChatProvider("Ollama");
        var graph = CouncilTestFactory.CreateAgentGraph();
        var reporter = new CouncilProgressReporter(null);

        var result = await graph.RunAsync(
            chat,
            ScanScenario.QuickHealthCheck,
            [InfoFinding("Windows 11")],
            "context",
            [],
            [],
            reporter,
            CancellationToken.None);

        Assert.False(result.UsedToolPath);
        Assert.Empty(result.ToolsInvoked);
        Assert.Equal(3, result.Messages.Count);
        Assert.All(result.Messages, m => Assert.Equal(CouncilPhaseType.Analysis, m.Phase));
        Assert.Contains("light path", result.ToolContextBlock, StringComparison.OrdinalIgnoreCase);
        Assert.True(chat.CompleteCallCount >= 4); // 3 debaters + chief
    }

    [Fact]
    public async Task ToolPath_InvokesBothReadOnlyToolsAndRunsDebate()
    {
        var knowledge = new TrackingKnowledgeRetrievalService();
        var hardware = new TrackingHardwareMonitor();
        var tools = new CouncilReadOnlyTools(knowledge, hardware);
        var graph = new CouncilAgentGraph(new SemanticKernelToolHost(tools));
        var chat = new ScriptedChatProvider("Ollama");
        var reporter = new CouncilProgressReporter(null);

        var result = await graph.RunAsync(
            chat,
            ScanScenario.AfterWindowsUpdate,
            [WarningFinding("Windows Update service not running")],
            "context",
            [],
            [],
            reporter,
            CancellationToken.None);

        Assert.True(result.UsedToolPath);
        Assert.Contains(CouncilReadOnlyTools.QueryKnowledgeBaseName, result.ToolsInvoked);
        Assert.Contains(CouncilReadOnlyTools.GetLocalStatusName, result.ToolsInvoked);
        Assert.True(knowledge.RetrieveCalls > 0);
        Assert.True(hardware.CaptureCalls > 0);
        Assert.Contains(result.Messages, m => m.Phase == CouncilPhaseType.Research);
        Assert.Contains(result.Messages, m => m.Phase == CouncilPhaseType.Debate);
        Assert.Contains(result.Messages, m => m.Phase == CouncilPhaseType.Rebuttal);
        Assert.DoesNotContain("light path", result.ToolContextBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pending", result.ToolContextBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chat.UserPrompts, p => p.Contains("tools pending", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(chat.UserPrompts.Where(p => p.Contains("PHASE: Analysis", StringComparison.Ordinal)),
            p => p.Contains("light path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetLocalStatus_OmitsDeviceNamesAndFindingTitles()
    {
        var hardware = new TrackingHardwareMonitor();
        var tools = new CouncilReadOnlyTools(new EmptyKnowledge(), hardware);
        var summary = CouncilReadOnlyTools.BuildFindingsSummaryJson(
        [
            new Finding
            {
                ModuleName = "Event logs",
                Title = @"Failure for alice on DESKTOP-PRIVATE at C:\Users\alice\app.exe",
                Details = "secret=sk-secret-value",
                Severity = FindingSeverity.Warning,
                Risk = FindingRisk.Medium
            }
        ]);

        var json = tools.GetLocalStatus(summary);

        Assert.DoesNotContain("Test CPU Brand", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESKTOP-PRIVATE", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Event logs", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("loadPercent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyTools_ExposeOnlyTwoKernelFunctions_NoWriteSurface()
    {
        var methods = typeof(CouncilReadOnlyTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<KernelFunctionAttribute>() is not null)
            .Select(m =>
            {
                var attr = m.GetCustomAttribute<KernelFunctionAttribute>()!;
                return string.IsNullOrWhiteSpace(attr.Name) ? m.Name : attr.Name;
            })
            .ToList();

        Assert.Equal(2, methods.Count);
        Assert.Contains(CouncilReadOnlyTools.QueryKnowledgeBaseName, methods);
        Assert.Contains(CouncilReadOnlyTools.GetLocalStatusName, methods);

        var typeNames = typeof(CouncilReadOnlyTools).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(CouncilReadOnlyTools).Namespace)
            .Select(t => t.Name)
            .ToList();
        Assert.DoesNotContain(typeNames, name => name.Contains("Write", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeNames, name => name.Contains("Optimize", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RulesPath_FillsDetailedExplanationAndStepEvidence()
    {
        var session = new LocalCouncilSession(new HealthScorePolicy());
        var reporter = new CouncilProgressReporter(null);
        var guide = await session.RunAsync(
            ScanScenario.QuickHealthCheck,
            [WarningFinding("Low disk space on C:")],
            [],
            [],
            [],
            reporter,
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(guide.DetailedExplanation));
        Assert.Contains("Why this plan", guide.DetailedExplanation!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(guide.Steps);
        Assert.All(guide.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.WhyThisMatters));
            Assert.False(string.IsNullOrWhiteSpace(step.Evidence));
        });
    }

    [Fact]
    public async Task LlmCouncil_ParsesDetailedExplanationAndStepWhyEvidence()
    {
        var chiefJson = """
            {
              "summary": "Fix disk pressure first.",
              "verdict": "Free space on C: before the next cumulative update.",
              "detailedExplanation": "Low free space blocks Windows Update staging on this PC.",
              "healthScore": 70,
              "steps": [
                {
                  "title": "Free disk space",
                  "instructions": "Open Storage Sense and clear temporary files, then empty Recycle Bin.",
                  "why": "Update packages need staging space.",
                  "evidence": "Scan reported low disk space on C:.",
                  "linkUrl": null,
                  "copyText": null
                }
              ]
            }
            """;

        var aiOptions = new AiOptions
        {
            ApiKey = "configured",
            Model = "test-model",
            ChatProvider = ChatProviderResolver.ModeOpenAi
        };

        var service = CouncilTestFactory.CreateCouncilService(
            new ChatProviderResolver(
                new OpenAiChatClient(new HttpClient(new ScriptedOpenAiHandler(chiefJson)), aiOptions),
                new OllamaChatProvider(new HttpClient(new RejectingHandler()), aiOptions),
                aiOptions),
            new DisabledWebSearch(),
            new EmptyKnowledge(),
            new HealthScorePolicy(),
            new NoOpEvaluation());

        var guide = await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [WarningFinding("Low disk space on C:")],
            allowExternalServices: true);

        Assert.Equal(OpenAiChatClient.ProviderName, guide.AiProviderName);
        Assert.False(string.IsNullOrWhiteSpace(guide.DetailedExplanation));
        Assert.Contains("staging", guide.DetailedExplanation!, StringComparison.OrdinalIgnoreCase);
        var step = Assert.Single(guide.Steps);
        Assert.Equal("Update packages need staging space.", step.WhyThisMatters);
        Assert.Equal("Scan reported low disk space on C:.", step.Evidence);
    }

    private static Finding InfoFinding(string title) => new()
    {
        ModuleName = "Operating system",
        Title = title,
        Details = "Informational baseline signal for tests.",
        Severity = FindingSeverity.Info,
        Risk = FindingRisk.NotApplicable
    };

    private static Finding WarningFinding(string title) => new()
    {
        ModuleName = title.Contains("disk", StringComparison.OrdinalIgnoreCase)
            ? "Disk space"
            : "Update services",
        Title = title,
        Details = "Warning-level finding used by Phase 3 graph tests.",
        Severity = FindingSeverity.Warning,
        Risk = FindingRisk.Medium,
        ActionState = FindingActionState.Recommended
    };

    private sealed class ScriptedChatProvider(string name, string? chiefOverride = null) : IChatCompletionProvider
    {
        public string Name { get; } = name;
        public bool IsAvailable => true;
        public int CompleteCallCount { get; private set; }
        public List<string> UserPrompts { get; } = [];

        public Task<string> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            IReadOnlyList<(string Role, string Content)>? priorTurns = null,
            CancellationToken cancellationToken = default)
        {
            CompleteCallCount++;
            UserPrompts.Add(userPrompt);
            if (systemPrompt.Contains("Chief Councilor", StringComparison.Ordinal))
            {
                return Task.FromResult(chiefOverride ?? """
                    {"summary":"Baseline only.","verdict":"No urgent work.","detailedExplanation":"Clean scan.","steps":[{"title":"Save baseline","instructions":"Record build number from Settings About page.","why":"Compare after next patch.","evidence":"Info-only findings."}]}
                    """);
            }

            return Task.FromResult("Headline\nOpinion grounded in the scan.");
        }
    }

    private sealed class ScriptedOpenAiHandler(string chiefJson) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var isChief = body.Contains("Chief Councilor", StringComparison.Ordinal);
            var message = isChief ? chiefJson : "Headline\nOpinion grounded in the scan.";
            var payload = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = message } }
                }
            });

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            };
        }
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }

    private sealed class TrackingKnowledgeRetrievalService : IKnowledgeRetrievalService
    {
        public int RetrieveCalls { get; private set; }

        public Task EnsureIndexedAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
            string query,
            int topK = 3,
            CancellationToken cancellationToken = default)
        {
            RetrieveCalls++;
            return Task.FromResult<IReadOnlyList<KnowledgeHit>>(
            [
                new KnowledgeHit
                {
                    Chunk = new KnowledgeChunk
                    {
                        Id = "t1",
                        PlaybookId = "update-services",
                        Title = "Update services",
                        Content = "Check wuauserv and BITS before retrying Windows Update."
                    },
                    Score = 0.91,
                    Query = query
                }
            ]);
        }

        public Task<IReadOnlyList<KnowledgeHit>> RetrieveForFindingsAsync(
            IReadOnlyList<Finding> findings,
            int topKPerQuery = 3,
            CancellationToken cancellationToken = default) =>
            RetrieveAsync(string.Join(" ", findings.Select(f => f.Title)), topKPerQuery, cancellationToken);
    }

    private sealed class TrackingHardwareMonitor : IHardwareMonitorService
    {
        public int CaptureCalls { get; private set; }

        public HardwareSnapshot Capture()
        {
            CaptureCalls++;
            return new HardwareSnapshot
            {
                CpuName = "Test CPU Brand",
                GpuName = "Test GPU Brand",
                CpuLoadPercent = 22,
                RamLoadPercent = 40
            };
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyKnowledge : IKnowledgeRetrievalService
    {
        public Task EnsureIndexedAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
            string query,
            int topK = 3,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeHit>>([]);

        public Task<IReadOnlyList<KnowledgeHit>> RetrieveForFindingsAsync(
            IReadOnlyList<Finding> findings,
            int topKPerQuery = 3,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeHit>>([]);
    }

    private sealed class DisabledWebSearch : IWebSearchService
    {
        public bool IsConfigured => false;

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WebSearchResult>>([]);
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
