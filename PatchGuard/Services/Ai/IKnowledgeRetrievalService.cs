using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public interface IKnowledgeRetrievalService
{
    Task EnsureIndexedAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        string query,
        int topK = 3,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeHit>> RetrieveForFindingsAsync(
        IReadOnlyList<Finding> findings,
        int topKPerQuery = 3,
        CancellationToken cancellationToken = default);
}
