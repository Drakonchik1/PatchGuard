using System.IO;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Settings;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class UserSettingsStoreTests
{
    [Fact]
    public void JsonStore_RoundTripsChatProvider()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonUserSettingsStore(path);
            store.Save(new UserSettings { ChatProvider = ChatProviderResolver.ModeOllama });

            var loaded = store.Load();
            Assert.Equal(ChatProviderResolver.ModeOllama, loaded.ChatProvider);
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
            var vm = new SettingsViewModel(new NoOpEvaluation(), options, store);

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

    [Theory]
    [InlineData("Cloud", "OpenAI")]
    [InlineData("openai", "OpenAI")]
    [InlineData("Auto", "Rules")]
    [InlineData("Ollama", "Ollama")]
    public void NormalizeProvider_MapsUiAliases(string input, string expected)
    {
        Assert.Equal(expected, SettingsViewModel.NormalizeProvider(input));
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
