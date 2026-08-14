namespace PatchGuard.Services.Ai;

/// <summary>
/// Picks Azure / OpenAI (cloud, consent-gated), Ollama (local), or null (rules-only council).
/// </summary>
public sealed class ChatProviderResolver
{
    public const string ModeAuto = "Auto";
    public const string ModeOpenAi = "OpenAI";
    public const string ModeAzure = "Azure";
    public const string ModeOllama = "Ollama";
    public const string ModeRules = "Rules";

    private readonly IChatCompletionProvider _openAi;
    private readonly IChatCompletionProvider _azure;
    private readonly IChatCompletionProvider _ollama;
    private readonly AiOptions _options;

    public ChatProviderResolver(
        OpenAiChatClient openAi,
        AzureOpenAiChatProvider azure,
        OllamaChatProvider ollama,
        AiOptions options)
    {
        _openAi = openAi;
        _azure = azure;
        _ollama = ollama;
        _options = options;
    }

    /// <summary>
    /// Resolves the chat backend.
    /// Azure and OpenAI require <paramref name="allowExternalServices"/>.
    /// Ollama does not — it never leaves the machine.
    /// <para>
    /// Auto order: Azure (configured + consent) → OpenAI (configured + consent) → Ollama → Rules.
    /// Bedrock is not part of Auto (stub only).
    /// </para>
    /// </summary>
    public IChatCompletionProvider? Resolve(bool allowExternalServices)
    {
        var mode = string.IsNullOrWhiteSpace(_options.ChatProvider)
            ? ModeAuto
            : _options.ChatProvider.Trim();

        return mode.ToUpperInvariant() switch
        {
            "RULES" => null,
            "OPENAI" or "CLOUD" => allowExternalServices && _openAi.IsAvailable ? _openAi : null,
            "AZURE" => allowExternalServices && _azure.IsAvailable ? _azure : null,
            "OLLAMA" => _ollama.IsAvailable ? _ollama : null,
            _ => ResolveAuto(allowExternalServices)
        };
    }

    private IChatCompletionProvider? ResolveAuto(bool allowExternalServices)
    {
        if (allowExternalServices && _azure.IsAvailable)
        {
            return _azure;
        }

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
