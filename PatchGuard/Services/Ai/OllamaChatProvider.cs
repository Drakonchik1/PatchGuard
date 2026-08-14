using System.Net;
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
    }

    public string Name => ProviderName;

    public bool IsAvailable =>
        _options.OllamaEnabled
        && !string.IsNullOrWhiteSpace(_options.OllamaBaseUrl)
        && !string.IsNullOrWhiteSpace(_options.OllamaModel)
        && IsLoopbackEndpoint(_options.OllamaBaseUrl);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(string Role, string Content)>? priorMessages = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.OllamaEnabled ||
            string.IsNullOrWhiteSpace(_options.OllamaModel) ||
            !TryGetLoopbackEndpoint(_options.OllamaBaseUrl, out var endpoint))
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
            messages,
            options = new
            {
                num_predict = Math.Clamp(_options.OllamaNumPredict, 64, 4096),
                num_ctx = Math.Clamp(_options.OllamaNumCtx, 2048, 32768),
                temperature = Math.Clamp(_options.OllamaTemperature, 0, 2)
            }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var requestUri = new Uri(endpoint, "api/chat");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await BoundedHttpResponse.ReadAsStreamAsync(response, cancellationToken);
        var parsed = await JsonSerializer.DeserializeAsync<OllamaChatResponse>(stream, JsonOptions, cancellationToken);

        var reply = parsed?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(reply))
        {
            throw new InvalidOperationException("Ollama returned an empty response.");
        }

        return reply;
    }

    public static bool IsLoopbackEndpoint(string? endpoint) =>
        TryGetLoopbackEndpoint(endpoint, out _);

    public static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "http://localhost:11434/" : trimmed + "/";
    }

    private static bool TryGetLoopbackEndpoint(string? endpoint, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            HasUserInfoDelimiter(endpoint.Trim()) ||
            !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) ||
            !IsLoopbackHost(candidate))
        {
            return false;
        }

        uri = new Uri(candidate.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }

    private static bool IsLoopbackHost(Uri uri)
    {
        if (string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return address.GetAddressBytes()[0] == 127;
        }

        return IPAddress.IPv6Loopback.Equals(address);
    }

    private static bool HasUserInfoDelimiter(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        var at = value.IndexOf('@', authorityStart);
        if (at < 0)
        {
            return false;
        }

        return IsBeforeDelimiter(value, at, authorityStart, '/') &&
               IsBeforeDelimiter(value, at, authorityStart, '\\') &&
               IsBeforeDelimiter(value, at, authorityStart, '?') &&
               IsBeforeDelimiter(value, at, authorityStart, '#');
    }

    private static bool IsBeforeDelimiter(string value, int position, int start, char delimiter)
    {
        var delimiterPosition = value.IndexOf(delimiter, start);
        return delimiterPosition < 0 || position < delimiterPosition;
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
