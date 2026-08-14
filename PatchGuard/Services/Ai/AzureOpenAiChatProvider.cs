using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Azure OpenAI chat completions behind <see cref="IChatCompletionProvider"/>.
/// Auth uses the <c>api-key</c> header (not Bearer).
/// </summary>
public sealed class AzureOpenAiChatProvider : IChatCompletionProvider
{
    public const string ProviderName = "Azure";
    public const string DefaultApiVersion = "2024-06-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public AzureOpenAiChatProvider(HttpClient httpClient, AiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => ProviderName;

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.AzureApiKey)
        && !string.IsNullOrWhiteSpace(_options.AzureEndpoint)
        && !string.IsNullOrWhiteSpace(_options.AzureDeployment)
        && TryNormalizeEndpoint(_options.AzureEndpoint, out _);

    public async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(string Role, string Content)>? priorMessages = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AzureApiKey) ||
            string.IsNullOrWhiteSpace(_options.AzureDeployment) ||
            !TryNormalizeEndpoint(_options.AzureEndpoint, out var endpoint))
        {
            throw new InvalidOperationException("Azure OpenAI is not configured (endpoint, deployment, and API key required).");
        }

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        if (priorMessages is not null)
        {
            foreach (var (role, content) in priorMessages)
            {
                messages.Add(new { role, content });
            }
        }

        messages.Add(new { role = "user", content = userPrompt });

        var body = new
        {
            temperature = 0.4,
            messages
        };

        var deployment = Uri.EscapeDataString(_options.AzureDeployment.Trim());
        var apiVersion = string.IsNullOrWhiteSpace(_options.AzureApiVersion)
            ? DefaultApiVersion
            : _options.AzureApiVersion.Trim();
        var relative =
            $"openai/deployments/{deployment}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
        var requestUri = new Uri(endpoint, relative);

        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("api-key", _options.AzureApiKey.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await BoundedHttpResponse.ReadAsStreamAsync(response, cancellationToken);
        var parsed = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, JsonOptions, cancellationToken);

        return parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim()
               ?? throw new InvalidOperationException("Azure OpenAI returned an empty response.");
    }

    public static bool TryNormalizeEndpoint(string? endpoint, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        var trimmed = endpoint.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var created) ||
            created.Scheme != Uri.UriSchemeHttps ||
            created.HostNameType != UriHostNameType.Dns ||
            !string.IsNullOrEmpty(created.UserInfo) ||
            HasUserInfoDelimiter(trimmed) ||
            !string.IsNullOrEmpty(created.Query) ||
            !string.IsNullOrEmpty(created.Fragment) ||
            !IsValidDnsHost(created.IdnHost) ||
            !IsOfficialAzureHost(created.IdnHost))
        {
            return false;
        }

        uri = new Uri(created.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        return true;
    }

    private static bool IsOfficialAzureHost(string host) =>
        IsSubdomain(host, "openai.azure.com") ||
        IsSubdomain(host, "services.ai.azure.com");

    private static bool IsSubdomain(string host, string suffix) =>
        host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidDnsHost(string host)
    {
        if (host.Length is 0 or > 253)
        {
            return false;
        }

        foreach (var label in host.Split('.'))
        {
            if (label.Length is 0 or > 63 ||
                !char.IsAsciiLetterOrDigit(label[0]) ||
                !char.IsAsciiLetterOrDigit(label[^1]) ||
                label.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }
        }

        return true;
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

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }
}
