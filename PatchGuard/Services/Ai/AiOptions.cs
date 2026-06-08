namespace PatchGuard.Services.Ai;

public sealed class AiOptions
{
    public const string OpenAiSection = "OpenAI";
    public const string WebSearchSection = "WebSearch";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string WebSearchProvider { get; set; } = "tavily";
    public string WebSearchApiKey { get; set; } = string.Empty;
}
