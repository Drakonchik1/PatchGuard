using System.IO;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Local RAG index over bundled playbooks.
/// Always uses local hashing embeddings so indexing never calls the network
/// (privacy: no consent required) and query/index dimensions stay consistent.
/// </summary>
public sealed class KnowledgeRetrievalService : IKnowledgeRetrievalService, IDisposable
{
    private readonly IEmbeddingService _embeddings;
    private readonly string _playbooksDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<KnowledgeChunk> _chunks = [];
    private bool _indexed;
    private bool _disposed;

    public KnowledgeRetrievalService(HashingEmbeddingService embeddings)
        : this(embeddings, ResolveDefaultPlaybooksDirectory())
    {
    }

    public KnowledgeRetrievalService(IEmbeddingService embeddings, string playbooksDirectory)
    {
        _embeddings = embeddings;
        _playbooksDirectory = playbooksDirectory;
    }

    public async Task EnsureIndexedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_indexed)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_indexed)
            {
                return;
            }

            var chunks = new List<KnowledgeChunk>();
            if (Directory.Exists(_playbooksDirectory))
            {
                foreach (var path in Directory.EnumerateFiles(_playbooksDirectory, "*.md")
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var playbookId = Path.GetFileNameWithoutExtension(path);
                    var markdown = await File.ReadAllTextAsync(path, cancellationToken);
                    chunks.AddRange(KnowledgeChunker.ChunkDocument(playbookId, markdown));
                }
            }

            if (chunks.Count > 0)
            {
                var vectors = await _embeddings.EmbedBatchAsync(
                    chunks.Select(c => $"{c.Title}\n{c.Content}").ToList(),
                    cancellationToken);
                if (vectors.Count != chunks.Count)
                {
                    throw new InvalidOperationException(
                        $"Embedding count mismatch: expected {chunks.Count}, got {vectors.Count}.");
                }

                for (var i = 0; i < chunks.Count; i++)
                {
                    chunks[i].Embedding = vectors[i];
                }
            }

            _chunks = chunks;
            _indexed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        string query,
        int topK = 3,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureIndexedAsync(cancellationToken);
        if (_chunks.Count == 0 || string.IsNullOrWhiteSpace(query) || topK <= 0)
        {
            return [];
        }

        var queryVector = await _embeddings.EmbedAsync(query, cancellationToken);
        return _chunks
            .Select(chunk => new KnowledgeHit
            {
                Chunk = chunk,
                Query = query,
                Score = CosineSimilarity(queryVector, chunk.Embedding ?? [])
            })
            .Where(hit => hit.Score > 0)
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToList();
    }

    public async Task<IReadOnlyList<KnowledgeHit>> RetrieveForFindingsAsync(
        IReadOnlyList<Finding> findings,
        int topKPerQuery = 3,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var queries = ExternalDiagnosticSanitizer.BuildSearchQueries(findings);
        var hits = new List<KnowledgeHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            foreach (var hit in await RetrieveAsync(query, topKPerQuery, cancellationToken))
            {
                if (seen.Add(hit.Chunk.Id))
                {
                    hits.Add(hit);
                }
            }
        }

        return hits
            .OrderByDescending(hit => hit.Score)
            .Take(Math.Max(topKPerQuery, 3))
            .ToList();
    }

    public static IReadOnlyList<KnowledgeReference> ToReferences(IReadOnlyList<KnowledgeHit> hits) =>
        hits.Select(hit => new KnowledgeReference
            {
                Title = hit.Chunk.Title,
                PlaybookId = hit.Chunk.PlaybookId,
                UsedFor = hit.Query,
                Score = Math.Round(hit.Score, 4)
            })
            .ToList();

    public static string FormatHits(IReadOnlyList<KnowledgeHit> hits) =>
        hits.Count == 0
            ? "(no local knowledge hits)"
            : string.Join(
                "\n\n",
                hits.Select(hit =>
                    $"[KB:{hit.Chunk.PlaybookId} | {hit.Chunk.Title} | score={hit.Score:F3}]\n{hit.Chunk.Content}"));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        if (magA <= 0 || magB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    private static string ResolveDefaultPlaybooksDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(baseDir, "KnowledgeBase", "Playbooks");
        if (Directory.Exists(bundled))
        {
            return bundled;
        }

        // Dev fallback when tests run without CopyToOutputDirectory yet.
        return Path.GetFullPath(Path.Combine(
            baseDir,
            "..", "..", "..", "..",
            "PatchGuard", "KnowledgeBase", "Playbooks"));
    }
}
