using System.IO;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Security;
using PatchGuard.Services.Settings;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class UserSettingsStoreTests
{
    [Fact]
    public void JsonStore_CorruptFileReturnsSafeDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ definitely not valid json");

            var loaded = new JsonUserSettingsStore(path).Load();

            Assert.Equal(new UserSettings().ChatProvider, loaded.ChatProvider);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void JsonStore_SaveReplacesThroughSameDirectoryTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "user-settings.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            store.Save(new UserSettings { ChatProvider = ChatProviderResolver.ModeRules });
            using var temporaryFileCreated = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(directory)
            {
                EnableRaisingEvents = true
            };
            watcher.Created += (_, args) =>
            {
                if (!args.Name!.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                {
                    temporaryFileCreated.Set();
                }
            };

            store.Save(new UserSettings { ChatProvider = ChatProviderResolver.ModeOllama });

            Assert.True(
                temporaryFileCreated.Wait(TimeSpan.FromSeconds(2)),
                "Expected a same-directory temporary file before atomic replacement.");
            Assert.Equal(ChatProviderResolver.ModeOllama, store.Load().ChatProvider);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void JsonStore_RoundTripsChatProviderAndAzureNonSecrets()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            store.Save(new UserSettings
            {
                ChatProvider = ChatProviderResolver.ModeAzure,
                AzureEndpoint = "https://example.openai.azure.com/",
                AzureDeployment = "gpt-deploy"
            });

            var loaded = store.Load();
            Assert.Equal(ChatProviderResolver.ModeAzure, loaded.ChatProvider);
            Assert.Equal("https://example.openai.azure.com/", loaded.AzureEndpoint);
            Assert.Equal("gpt-deploy", loaded.AzureDeployment);

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SettingsViewModel_PersistsProviderChoiceToStoreAndAiOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var options = new AiOptions { ChatProvider = ChatProviderResolver.ModeRules };
            var vm = new SettingsViewModel(new NoOpEvaluation(), options, store, new MemorySecretStore());

            vm.OnNavigatedTo();
            Assert.Equal(ChatProviderResolver.ModeRules, vm.SelectedChatProvider);

            vm.SelectedChatProvider = ChatProviderResolver.ModeOllama;

            Assert.Equal(ChatProviderResolver.ModeOllama, options.ChatProvider);
            Assert.Equal(ChatProviderResolver.ModeOllama, store.Load().ChatProvider);
            Assert.Contains("Ollama", vm.ProviderStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SettingsViewModel_SaveAzureSettings_StoresKeyInSecretServiceNotJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            var secrets = new MemorySecretStore();
            var options = new AiOptions { ChatProvider = ChatProviderResolver.ModeAzure };
            var vm = new SettingsViewModel(new NoOpEvaluation(), options, store, secrets);

            vm.OnNavigatedTo();
            vm.SelectedChatProvider = ChatProviderResolver.ModeAzure;
            vm.AzureEndpoint = "https://example.openai.azure.com/";
            vm.AzureDeployment = "gpt-deploy";
            vm.AzureApiKeyInput = "azure-live-key";
            vm.SaveAzureSettingsCommand.Execute(null);

            Assert.Equal("azure-live-key", options.AzureApiKey);
            Assert.Equal("azure-live-key", secrets.GetSecret(SecretKeys.AzureOpenAiApiKey));
            Assert.Equal(string.Empty, vm.AzureApiKeyInput);

            var json = File.ReadAllText(path);
            Assert.Contains("gpt-deploy", json, StringComparison.Ordinal);
            Assert.DoesNotContain("azure-live-key", json, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("Cloud", "OpenAI")]
    [InlineData("openai", "OpenAI")]
    [InlineData("Azure", "Azure")]
    [InlineData("Auto", "Rules")]
    [InlineData("Ollama", "Ollama")]
    public void NormalizeProvider_MapsUiAliases(string input, string expected)
    {
        Assert.Equal(expected, SettingsViewModel.NormalizeProvider(input));
    }

    private sealed class MemorySecretStore : ISecretStorageService
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

        public string? GetSecret(string key) =>
            _map.TryGetValue(key, out var value) ? value : null;

        public void SetSecret(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _map.Remove(key);
                return;
            }

            _map[key] = value.Trim();
        }

        public bool HasSecret(string key) => _map.ContainsKey(key);
    }

    private sealed class NoOpEvaluation : ICouncilEvaluationService
    {
        public Task SaveAsync(
            ScanScenario scenario,
            RepairGuide guide,
            TimeSpan latency,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<CouncilEvaluationRecord>> GetRecentAsync(
            int take = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CouncilEvaluationRecord>>([]);
    }
}
