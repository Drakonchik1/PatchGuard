namespace PatchGuard.Services.Ai;

public sealed class KnowledgeChunk
{
    public required string Id { get; init; }
    public required string PlaybookId { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public float[]? Embedding { get; set; }
}

public sealed class KnowledgeHit
{
    public required KnowledgeChunk Chunk { get; init; }
    public required double Score { get; init; }
    public required string Query { get; init; }
}
