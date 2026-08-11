using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Ai.Tools;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

internal static class CouncilTestFactory
{
    public static CouncilAgentGraph CreateAgentGraph(
        IKnowledgeRetrievalService? knowledge = null,
        IHardwareMonitorService? hardware = null)
    {
        var tools = new CouncilReadOnlyTools(
            knowledge ?? new EmptyKnowledgeRetrievalService(),
            hardware ?? new StubHardwareMonitor());
        return new CouncilAgentGraph(new SemanticKernelToolHost(tools));
    }

    public static AiCouncilService CreateCouncilService(
        ChatProviderResolver resolver,
        IWebSearchService webSearch,
        IKnowledgeRetrievalService knowledge,
        IHealthScorePolicy healthScorePolicy,
        ICouncilEvaluationService evaluationService,
        IHardwareMonitorService? hardware = null) =>
        new(
            resolver,
            webSearch,
            knowledge,
            healthScorePolicy,
            evaluationService,
            CreateAgentGraph(knowledge, hardware));

    private sealed class EmptyKnowledgeRetrievalService : IKnowledgeRetrievalService
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

    private sealed class StubHardwareMonitor : IHardwareMonitorService
    {
        public HardwareSnapshot Capture() => new()
        {
            CpuName = "Test CPU",
            CpuLoadPercent = 12,
            RamUsedGb = 8,
            RamTotalGb = 16,
            RamLoadPercent = 50
        };

        public void Dispose()
        {
        }
    }
}
