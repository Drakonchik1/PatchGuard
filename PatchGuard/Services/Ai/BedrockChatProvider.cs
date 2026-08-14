namespace PatchGuard.Services.Ai;

/// <summary>
/// Honest AWS Bedrock stub — not wired into Auto resolution.
/// See docs/CLOUD_ARCHITECTURE.md and README for scope.
/// </summary>
public sealed class BedrockChatProvider : IChatCompletionProvider
{
    public const string ProviderName = "Bedrock";

    public string Name => ProviderName;

    /// <summary>Always false until a real Bedrock adapter ships.</summary>
    public bool IsAvailable => false;

    public Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(string Role, string Content)>? priorMessages = null,
        CancellationToken cancellationToken = default)
    {
        _ = systemPrompt;
        _ = userPrompt;
        _ = priorMessages;
        _ = cancellationToken;
        return Task.FromException<string>(new NotSupportedException(
            "AWS Bedrock is not implemented in PatchGuard. " +
            "Use Ollama (local), OpenAI, or Azure OpenAI. See docs/CLOUD_ARCHITECTURE.md."));
    }
}
