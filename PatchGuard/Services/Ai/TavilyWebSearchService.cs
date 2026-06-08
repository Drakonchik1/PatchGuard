using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PatchGuard.Services.Ai;

public sealed class TavilyWebSearchService : IWebSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public TavilyWebSearchService(HttpClient httpClient, AiOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.WebSearchApiKey);

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var body = new
        {
            api_key = _options.WebSearchApiKey,
            query,
            max_results = 5,
            search_depth = "basic"
        };

        using var response = await _httpClient.PostAsync(
            "https://api.tavily.com/search",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parsed = await JsonSerializer.DeserializeAsync<TavilyResponse>(stream, JsonOptions, cancellationToken);

        return parsed?.Results?
            .Select(r => new WebSearchResult
            {
                Title = r.Title ?? "Result",
                Url = r.Url ?? string.Empty,
                Snippet = r.Content ?? string.Empty
            })
            .ToList() ?? [];
    }

    private sealed class TavilyResponse
    {
        public List<TavilyResult>? Results { get; set; }
    }

    private sealed class TavilyResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Content { get; set; }
    }
}
