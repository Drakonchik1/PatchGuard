using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Optional OpenAI embeddings. Not used for local KB indexing (privacy + dimension stability).
/// Available for future cloud-assisted features when the caller has consent and an API key.
/// </summary>
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private const int MaxInputChars = 8_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;
    private readonly IEmbeddingService _fallback;

    public OpenAiEmbeddingService(HttpClient httpClient, AiOptions options, HashingEmbeddingService fallback)
    {
        _httpClient = httpClient;
        _options = options;
        _fallback = fallback;
        _httpClient.BaseAddress ??= new Uri("https://api.openai.com/v1/");
        if (!string.IsNullOrWhiteSpace(options.ApiKey) &&
            _httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) || _fallback.IsConfigured;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync([text], cancellationToken);
        return batch.Count > 0 ? batch[0] : await _fallback.EmbedAsync(text, cancellationToken);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return await _fallback.EmbedBatchAsync(texts, cancellationToken);
        }

        if (texts.Count == 0)
        {
            return [];
        }

        var truncated = texts
            .Select(text => text.Length <= MaxInputChars ? text : text[..MaxInputChars])
            .ToList();

        var body = new
        {
            model = _options.EmbeddingModel,
            input = truncated
        };

        try
        {
            using var response = await _httpClient.PostAsync(
                "embeddings",
                new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return await _fallback.EmbedBatchAsync(texts, cancellationToken);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<EmbeddingResponse>(stream, JsonOptions, cancellationToken);
            if (parsed?.Data is null || parsed.Data.Count == 0)
            {
                return await _fallback.EmbedBatchAsync(texts, cancellationToken);
            }

            return parsed.Data
                .OrderBy(item => item.Index)
                .Select(item => item.Embedding ?? [])
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await _fallback.EmbedBatchAsync(texts, cancellationToken);
        }
    }

    private sealed class EmbeddingResponse
    {
        public List<EmbeddingData>? Data { get; set; }
    }

    private sealed class EmbeddingData
    {
        public int Index { get; set; }
        public float[]? Embedding { get; set; }
    }
}
