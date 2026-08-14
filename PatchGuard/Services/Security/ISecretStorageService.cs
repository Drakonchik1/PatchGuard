namespace PatchGuard.Services.Security;

/// <summary>
/// At-rest secret storage. Implementations must not write plaintext keys to user-editable JSON.
/// </summary>
public interface ISecretStorageService
{
    /// <summary>Returns plaintext secret, or null when missing/unreadable.</summary>
    string? GetSecret(string key);

    /// <summary>Protects and persists <paramref name="value"/>. Empty/whitespace deletes the secret.</summary>
    void SetSecret(string key, string? value);

    bool HasSecret(string key);
}

/// <summary>Well-known secret key names for PatchGuard cloud adapters.</summary>
public static class SecretKeys
{
    public const string OpenAiApiKey = "openai-api-key";
    public const string AzureOpenAiApiKey = "azure-openai-api-key";
    public const string WebSearchApiKey = "websearch-api-key";
}
