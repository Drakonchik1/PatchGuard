namespace PatchGuard.Services.Ai;

public sealed class AiOptions
{
    public const string OpenAiSection = "OpenAI";
    public const string WebSearchSection = "WebSearch";
    public const string OllamaSection = "Ollama";
    public const string AiSection = "Ai";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string WebSearchProvider { get; set; } = "tavily";
    public string WebSearchApiKey { get; set; } = string.Empty;

    /// <summary>Auto | OpenAI | Ollama | Rules</summary>
    public string ChatProvider { get; set; } = "Auto";

    /// <summary>Default false so unit tests stay offline unless explicitly enabled.</summary>
    public bool OllamaEnabled { get; set; }

    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "qwen3.5:latest";
}
