using Microsoft.Extensions.Configuration;
using PatchGuard.Services.Ai;

namespace PatchGuard.Services.Security;

/// <summary>
/// Loads cloud API keys from DPAPI storage, migrating once from plain configuration when needed.
/// </summary>
public static class SecretBootstrap
{
    public static void ApplySecrets(
        AiOptions options,
        ISecretStorageService secrets,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(configuration);

        options.ApiKey = ResolveAndMigrate(
            secrets,
            SecretKeys.OpenAiApiKey,
            configuration[$"{AiOptions.OpenAiSection}:ApiKey"]);

        options.WebSearchApiKey = ResolveAndMigrate(
            secrets,
            SecretKeys.WebSearchApiKey,
            configuration[$"{AiOptions.WebSearchSection}:ApiKey"]);

        options.AzureApiKey = ResolveAndMigrate(
            secrets,
            SecretKeys.AzureOpenAiApiKey,
            configuration[$"{AiOptions.AzureOpenAiSection}:ApiKey"]);
    }

    /// <summary>
    /// Prefer DPAPI store; if empty and configuration has a value, migrate into DPAPI then return it.
    /// </summary>
    public static string ResolveAndMigrate(
        ISecretStorageService secrets,
        string secretKey,
        string? configurationValue)
    {
        var stored = secrets.GetSecret(secretKey);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored.Trim();
        }

        if (string.IsNullOrWhiteSpace(configurationValue))
        {
            return string.Empty;
        }

        var trimmed = configurationValue.Trim();
        secrets.SetSecret(secretKey, trimmed);
        return trimmed;
    }
}
