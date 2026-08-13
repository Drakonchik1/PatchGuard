namespace PatchGuard.Services.Settings;

public interface IUserSettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

public sealed class UserSettings
{
    /// <summary>OpenAI | Ollama | Rules | Auto</summary>
    public string ChatProvider { get; set; } = "Auto";
}
