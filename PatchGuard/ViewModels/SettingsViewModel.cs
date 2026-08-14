using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Data.Entities;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Security;
using PatchGuard.Services.Settings;

namespace PatchGuard.ViewModels;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    public const string ProviderCloud = ChatProviderResolver.ModeOpenAi;
    public const string ProviderAzure = ChatProviderResolver.ModeAzure;
    public const string ProviderOllama = ChatProviderResolver.ModeOllama;
    public const string ProviderRules = ChatProviderResolver.ModeRules;

    private readonly ICouncilEvaluationService _evaluationService;
    private readonly AiOptions _aiOptions;
    private readonly IUserSettingsStore _userSettings;
    private readonly ISecretStorageService _secrets;
    private bool _suppressProviderPersist;

    public SettingsViewModel(
        ICouncilEvaluationService evaluationService,
        AiOptions aiOptions,
        IUserSettingsStore userSettings,
        ISecretStorageService secrets)
    {
        _evaluationService = evaluationService;
        _aiOptions = aiOptions;
        _userSettings = userSettings;
        _secrets = secrets;
    }

    public ObservableCollection<CouncilEvaluationRecord> RecentSessions { get; } = [];

    [ObservableProperty]
    private bool _hasRecentSessions;

    [ObservableProperty]
    private string _selectedChatProvider = ProviderRules;

    [ObservableProperty]
    private string _providerStatus = string.Empty;

    [ObservableProperty]
    private string _azureEndpoint = string.Empty;

    [ObservableProperty]
    private string _azureDeployment = string.Empty;

    [ObservableProperty]
    private string _azureApiKeyInput = string.Empty;

    [ObservableProperty]
    private string _azureSecretStatus = string.Empty;

    [ObservableProperty]
    private bool _isAzurePanelVisible;

    public string Title => "Settings";

    public void OnNavigatedTo()
    {
        _suppressProviderPersist = true;
        SelectedChatProvider = NormalizeProvider(_aiOptions.ChatProvider);
        AzureEndpoint = _aiOptions.AzureEndpoint;
        AzureDeployment = _aiOptions.AzureDeployment;
        AzureApiKeyInput = string.Empty;
        AzureSecretStatus = _secrets.HasSecret(SecretKeys.AzureOpenAiApiKey)
            || !string.IsNullOrWhiteSpace(_aiOptions.AzureApiKey)
            ? "API key is stored via Windows DPAPI (not in user-settings.json)."
            : "No Azure API key stored yet.";
        IsAzurePanelVisible = string.Equals(SelectedChatProvider, ProviderAzure, StringComparison.Ordinal);
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
        PersistUserSettings(normalized);
        IsAzurePanelVisible = string.Equals(normalized, ProviderAzure, StringComparison.Ordinal);
        ProviderStatus = DescribeProvider(normalized);
    }

    [RelayCommand]
    private void SaveAzureSettings()
    {
        _aiOptions.AzureEndpoint = AzureEndpoint?.Trim() ?? string.Empty;
        _aiOptions.AzureDeployment = AzureDeployment?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(AzureApiKeyInput))
        {
            var key = AzureApiKeyInput.Trim();
            _secrets.SetSecret(SecretKeys.AzureOpenAiApiKey, key);
            _aiOptions.AzureApiKey = key;
            AzureApiKeyInput = string.Empty;
            AzureSecretStatus = "API key saved with Windows DPAPI.";
        }

        PersistUserSettings(_aiOptions.ChatProvider);
        ProviderStatus = DescribeProvider(SelectedChatProvider);
    }

    public static string NormalizeProvider(string? value)
    {
        var mode = string.IsNullOrWhiteSpace(value) ? ProviderRules : value.Trim();
        return mode.ToUpperInvariant() switch
        {
            "OPENAI" or "CLOUD" => ProviderCloud,
            "AZURE" => ProviderAzure,
            "OLLAMA" => ProviderOllama,
            "RULES" => ProviderRules,
            // Settings radio has no Auto tile — map to Rules for display; runtime Auto stays in appsettings until user picks.
            "AUTO" => ProviderRules,
            _ => ProviderRules
        };
    }

    private void PersistUserSettings(string chatProvider)
    {
        _userSettings.Save(new UserSettings
        {
            ChatProvider = chatProvider,
            AzureEndpoint = _aiOptions.AzureEndpoint,
            AzureDeployment = _aiOptions.AzureDeployment
        });
    }

    private static string DescribeProvider(string provider) => provider switch
    {
        ProviderCloud => "Cloud (OpenAI) — requires API key (DPAPI) and consent for each Guide run.",
        ProviderAzure => "Azure OpenAI — endpoint + deployment + DPAPI key; consent required.",
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
