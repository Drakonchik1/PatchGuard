using System.IO;
using System.Text.Json;

namespace PatchGuard.Services.Settings;

/// <summary>
/// Persists lightweight user preferences under %LocalAppData%/PatchGuard.
/// </summary>
public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _gate = new();

    public JsonUserSettingsStore()
        : this(ResolveDefaultPath())
    {
    }

    public JsonUserSettingsStore(string path)
    {
        _path = path;
    }

    public UserSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new UserSettings();
                }

                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            }
            catch
            {
                return new UserSettings();
            }
        }
    }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_path, json);
        }
    }

    private static string ResolveDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatchGuard",
            "user-settings.json");
}
