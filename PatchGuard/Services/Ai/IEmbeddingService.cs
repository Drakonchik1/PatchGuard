namespace PatchGuard.Services.Ai;

public interface IEmbeddingService
{
    /// <summary>True when this implementation can produce real (or local) vectors.</summary>
    bool IsConfigured { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
