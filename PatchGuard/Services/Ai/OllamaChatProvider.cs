using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Local Ollama chat — data stays on-machine (no cloud API key).
/// </summary>
public sealed class OllamaChatProvider : IChatCompletionProvider
{
    public const string ProviderName = "Ollama";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public OllamaChatProvider(HttpClient httpClient, AiOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (_httpClient.BaseAddress is null &&
            Uri.TryCreate(NormalizeBaseUrl(options.OllamaBaseUrl), UriKind.Absolute, out var baseUri))
        {
            _httpClient.BaseAddress = baseUri;
        }
    }

    public string Name => ProviderName;

    public bool IsAvailable =>
        _options.OllamaEnabled
        && !string.IsNullOrWhiteSpace(_options.OllamaBaseUrl)
        && !string.IsNullOrWhiteSpace(_options.OllamaModel);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(string Role, string Content)>? priorMessages = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Ollama is not enabled or configured.");
        }

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (priorMessages is not null)
        {
            foreach (var (role, priorContent) in priorMessages)
            {
                messages.Add(new { role, content = priorContent });
            }
        }

        messages.Add(new { role = "user", content = userPrompt });

        var body = new
        {
            model = _options.OllamaModel,
            stream = false,
            messages
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var response = await _httpClient.PostAsync(
            "api/chat",
            new StringContent(json, Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parsed = await JsonSerializer.DeserializeAsync<OllamaChatResponse>(stream, JsonOptions, cancellationToken);

        var reply = parsed?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        return reply;
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "http://localhost:11434/" : trimmed + "/";
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Content { get; set; }
    }
}
