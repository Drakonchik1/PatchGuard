using System.Security.Cryptography;
using System.Text;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Deterministic local embeddings for offline/dev when OpenAI is not configured.
/// Not semantic — only stable vectors so retrieval plumbing can be built and tested.
/// </summary>
public sealed class HashingEmbeddingService : IEmbeddingService
{
    public const int Dimensions = 64;

    public bool IsConfigured => true;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Embed(text));
    }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<float[]> vectors = texts.Select(Embed).ToList();
        return Task.FromResult(vectors);
    }

    private static float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        var tokens = text
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var index = hash[0] % Dimensions;
            var sign = (hash[1] & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        // L2-normalize so cosine similarity stays well-defined.
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }

        return vector;
    }
}
