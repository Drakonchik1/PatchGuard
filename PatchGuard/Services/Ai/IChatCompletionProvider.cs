namespace PatchGuard.Services.Ai;

/// <summary>
/// Shared chat backend for the AI council — OpenAI, Azure OpenAI, or Ollama (local).
/// </summary>
public interface IChatCompletionProvider
{
    string Name { get; }

    bool IsAvailable { get; }

    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<(string Role, string Content)>? priorMessages = null,
        CancellationToken cancellationToken = default);
}
