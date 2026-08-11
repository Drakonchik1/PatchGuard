namespace PatchGuard.Services.Ai;

/// <summary>
/// Picks OpenAI (cloud, consent-gated), Ollama (local), or null (rules-only council).
/// </summary>
public sealed class ChatProviderResolver
{
    public const string ModeAuto = "Auto";
    public const string ModeOpenAi = "OpenAI";
    public const string ModeOllama = "Ollama";
    public const string ModeRules = "Rules";

    private readonly IChatCompletionProvider _openAi;
    private readonly IChatCompletionProvider _ollama;
    private readonly AiOptions _options;

    public ChatProviderResolver(
        OpenAiChatClient openAi,
        OllamaChatProvider ollama,
        AiOptions options)
    {
        _openAi = openAi;
        _ollama = ollama;
        _options = options;
    }

    /// <summary>
    /// Resolves the chat backend. OpenAI requires <paramref name="allowExternalServices"/>.
    /// Ollama does not — it never leaves the machine.
    /// </summary>
    public IChatCompletionProvider? Resolve(bool allowExternalServices)
    {
        var mode = string.IsNullOrWhiteSpace(_options.ChatProvider)
            ? ModeAuto
            : _options.ChatProvider.Trim();

        return mode.ToUpperInvariant() switch
        {
            "RULES" => null,
            "OPENAI" => allowExternalServices && _openAi.IsAvailable ? _openAi : null,
            "OLLAMA" => _ollama.IsAvailable ? _ollama : null,
            _ => ResolveAuto(allowExternalServices)
        };
    }

    private IChatCompletionProvider? ResolveAuto(bool allowExternalServices)
    {
        if (allowExternalServices && _openAi.IsAvailable)
        {
            return _openAi;
        }

        if (_ollama.IsAvailable)
        {
            return _ollama;
        }

        return null;
    }
}
