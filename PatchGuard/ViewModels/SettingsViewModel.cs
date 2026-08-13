using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PatchGuard.Data.Entities;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Settings;

namespace PatchGuard.ViewModels;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    public const string ProviderCloud = ChatProviderResolver.ModeOpenAi;
    public const string ProviderOllama = ChatProviderResolver.ModeOllama;
    public const string ProviderRules = ChatProviderResolver.ModeRules;

    private readonly ICouncilEvaluationService _evaluationService;
    private readonly AiOptions _aiOptions;
    private readonly IUserSettingsStore _userSettings;
    private bool _suppressProviderPersist;

    public SettingsViewModel(
        ICouncilEvaluationService evaluationService,
        AiOptions aiOptions,
        IUserSettingsStore userSettings)
    {
        _evaluationService = evaluationService;
        _aiOptions = aiOptions;
        _userSettings = userSettings;
    }

    public ObservableCollection<CouncilEvaluationRecord> RecentSessions { get; } = [];

    [ObservableProperty]
    private bool _hasRecentSessions;

    [ObservableProperty]
    private string _selectedChatProvider = ProviderRules;

    [ObservableProperty]
    private string _providerStatus = string.Empty;

    public string Title => "Settings";

    public void OnNavigatedTo()
    {
        _suppressProviderPersist = true;
        SelectedChatProvider = NormalizeProvider(_aiOptions.ChatProvider);
        _suppressProviderPersist = false;
        ProviderStatus = DescribeProvider(SelectedChatProvider);
        _ = LoadRecentSessionsAsync();
    }

    partial void OnSelectedChatProviderChanged(string value)
    {
        if (_suppressProviderPersist || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = NormalizeProvider(value);
        _aiOptions.ChatProvider = normalized;
        _userSettings.Save(new UserSettings { ChatProvider = normalized });
        ProviderStatus = DescribeProvider(normalized);
    }

    public static string NormalizeProvider(string? value)
    {
        var mode = string.IsNullOrWhiteSpace(value) ? ProviderRules : value.Trim();
        return mode.ToUpperInvariant() switch
        {
            "OPENAI" or "CLOUD" => ProviderCloud,
            "OLLAMA" => ProviderOllama,
            "RULES" => ProviderRules,
            // Settings radio is Cloud / Ollama / Rules — Auto maps to Rules until Sprint 6.
            "AUTO" => ProviderRules,
            _ => ProviderRules
        };
    }

    private static string DescribeProvider(string provider) => provider switch
    {
        ProviderCloud => "Cloud (OpenAI) — requires API key and consent for each Guide run.",
        ProviderOllama => "Ollama — local LLM on this PC; no cloud consent required.",
        _ => "Rules — deterministic local council only (no LLM)."
    };

    private async Task LoadRecentSessionsAsync()
    {
        RecentSessions.Clear();

        var records = await _evaluationService.GetRecentAsync();
        foreach (var record in records)
        {
            RecentSessions.Add(record);
        }

        HasRecentSessions = RecentSessions.Count > 0;
    }
}
