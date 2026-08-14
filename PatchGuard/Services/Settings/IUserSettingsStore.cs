namespace PatchGuard.Services.Settings;

public interface IUserSettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

public sealed class UserSettings
{
    /// <summary>OpenAI | Azure | Ollama | Rules | Auto</summary>
    public string ChatProvider { get; set; } = "Auto";

    /// <summary>Non-secret Azure endpoint (API key lives in DPAPI secret store).</summary>
    public string AzureEndpoint { get; set; } = string.Empty;

    /// <summary>Non-secret Azure deployment name.</summary>
    public string AzureDeployment { get; set; } = string.Empty;
}
