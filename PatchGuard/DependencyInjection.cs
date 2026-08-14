using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatchGuard.Data;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Ai.Tools;
using PatchGuard.Services.Alerts;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.Fixes;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.Health;
using PatchGuard.Services.History;
using PatchGuard.Services.Ml;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Optimization;
using PatchGuard.Services.Optimization.Steps;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;
using PatchGuard.Services.Security;
using PatchGuard.Services.Settings;
using PatchGuard.ViewModels;

namespace PatchGuard;

public static class DependencyInjection
{
    public static IServiceCollection AddPatchGuard(this IServiceCollection services, IConfiguration configuration)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PatchGuard",
            "patchguard.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var aiOptions = new AiOptions
        {
            ApiKey = string.Empty,
            Model = configuration[$"{AiOptions.OpenAiSection}:Model"] ?? "gpt-4o-mini",
            EmbeddingModel = configuration[$"{AiOptions.OpenAiSection}:EmbeddingModel"] ?? "text-embedding-3-small",
            WebSearchProvider = configuration[$"{AiOptions.WebSearchSection}:Provider"] ?? "tavily",
            WebSearchApiKey = string.Empty,
            AzureEndpoint = configuration[$"{AiOptions.AzureOpenAiSection}:Endpoint"] ?? string.Empty,
            AzureDeployment = configuration[$"{AiOptions.AzureOpenAiSection}:Deployment"] ?? string.Empty,
            AzureApiKey = string.Empty,
            AzureApiVersion = configuration[$"{AiOptions.AzureOpenAiSection}:ApiVersion"]
                ?? AzureOpenAiChatProvider.DefaultApiVersion,
            ChatProvider = configuration[$"{AiOptions.AiSection}:ChatProvider"]
                ?? ChatProviderResolver.ModeAuto,
            OllamaEnabled = bool.TryParse(
                configuration[$"{AiOptions.OllamaSection}:Enabled"], out var ollamaEnabled)
                && ollamaEnabled,
            OllamaBaseUrl = configuration[$"{AiOptions.OllamaSection}:BaseUrl"]
                ?? "http://localhost:11434",
            OllamaModel = configuration[$"{AiOptions.OllamaSection}:Model"]
                ?? "llama3.2:3b",
            OllamaNumPredict = int.TryParse(
                configuration[$"{AiOptions.OllamaSection}:NumPredict"], out var numPredict)
                ? numPredict
                : 512,
            OllamaNumCtx = int.TryParse(
                configuration[$"{AiOptions.OllamaSection}:NumCtx"], out var numCtx)
                ? numCtx
                : 4096,
            OllamaTemperature = double.TryParse(
                configuration[$"{AiOptions.OllamaSection}:Temperature"],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var temperature)
                ? temperature
                : 0.35
        };

        var secretStorage = new DpapiSecretStorageService();
        SecretBootstrap.ApplySecrets(aiOptions, secretStorage, configuration);
        services.AddSingleton<ISecretStorageService>(secretStorage);

        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        var userSettings = new JsonUserSettingsStore().Load();
        if (!string.IsNullOrWhiteSpace(userSettings.ChatProvider))
        {
            aiOptions.ChatProvider = userSettings.ChatProvider.Trim();
        }

        if (!string.IsNullOrWhiteSpace(userSettings.AzureEndpoint))
        {
            aiOptions.AzureEndpoint = userSettings.AzureEndpoint.Trim();
        }

        if (!string.IsNullOrWhiteSpace(userSettings.AzureDeployment))
        {
            aiOptions.AzureDeployment = userSettings.AzureDeployment.Trim();
        }

        services.AddSingleton(aiOptions);

        services.AddDbContextFactory<PatchGuardDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddHttpClient<OpenAiChatClient>();
        services.AddHttpClient<AzureOpenAiChatProvider>((_, client) =>
        {
            if (AzureOpenAiChatProvider.TryNormalizeEndpoint(aiOptions.AzureEndpoint, out var baseUri))
            {
                client.BaseAddress = baseUri;
            }

            client.Timeout = TimeSpan.FromMinutes(3);
        });
        services.AddHttpClient<OllamaChatProvider>((_, client) =>
        {
            client.BaseAddress = new Uri(OllamaChatProvider.NormalizeBaseUrl(aiOptions.OllamaBaseUrl));
            // Small local models still need headroom for ~13 sequential council calls.
            client.Timeout = TimeSpan.FromMinutes(8);
        });
        services.AddHttpClient<OpenAiEmbeddingService>();
        services.AddHttpClient<TavilyWebSearchService>();
        services.AddSingleton<BedrockChatProvider>();
        services.AddSingleton<ChatProviderResolver>();

        // Local KB must stay offline: hashing embeddings only (no OpenAI upload without consent).
        services.AddSingleton<HashingEmbeddingService>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<HashingEmbeddingService>());
        services.AddSingleton<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
        services.AddSingleton<CouncilReadOnlyTools>();
        services.AddSingleton<SemanticKernelToolHost>();
        services.AddSingleton<CouncilAgentGraph>();

        services.AddSingleton<ScanSessionState>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IViewModelHost>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<INavigationService, NavigationService>();

        // Platform + hardware services
        services.AddSingleton<IAdminElevationService, AdminElevationService>();
        services.AddSingleton<IUserConfirmationService, WpfUserConfirmationService>();
        services.AddSingleton<IOsThermalTemperatureSource, WindowsThermalZoneTemperatureSource>();
        services.AddSingleton<IHardwareMonitorService, LibreHardwareMonitorService>();
        services.AddSingleton<IFpsCaptureService, PresentMonFpsCaptureService>();

        // Optimizer steps run in registration order.
        services.AddSingleton<IOptimizationStep, WorkingSetTrimStep>();
        services.AddSingleton<IOptimizationStep, TempFilesCleanStep>();
        services.AddSingleton<IOptimizationStep, RecycleBinStep>();
        services.AddSingleton<IOptimizationStep, DnsFlushStep>();
        services.AddSingleton<IOptimizationStep, ExplorerRestartStep>();
        services.AddSingleton<ISystemOptimizerService, SystemOptimizerService>();
        services.AddSingleton<IGuidedFixPlanService, GuidedFixPlanService>();

        // Diagnostic modules (registration order is the scan/display order).
        services.AddSingleton<IDiagnosticModule, OsInfoDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, DiskSpaceDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, MemoryLoadDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, TemperatureDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, CpuLoadDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, GpuInfoDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, WindowsUpdateHistoryDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, EventLogDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, UpdateServicesDiagnosticModule>();
        services.AddSingleton<IDiagnosticModule, AnomalyDiagnosticModule>();

        services.AddSingleton<IDiagnosticOrchestrator, DiagnosticOrchestrator>();
        services.AddSingleton<IHealthScorePolicy, HealthScorePolicy>();
        services.AddSingleton<DatabaseSchemaInitializer>();
        services.AddSingleton<CouncilEvaluator>();
        services.AddSingleton<IWebSearchService, TavilyWebSearchService>();
        services.AddSingleton<ICouncilEvaluationService, CouncilEvaluationService>();
        services.AddSingleton<IAiCouncilService, AiCouncilService>();
        services.AddSingleton<IScanHistoryService, ScanHistoryService>();
        services.AddSingleton<IPerformanceHistoryService, PerformanceHistoryService>();
        services.AddSingleton<ISensorHistoryService, SensorHistoryService>();
        services.AddSingleton<IAlertRuleEngine, AlertRuleEngine>();
        services.AddSingleton<ZScoreAnomalyDetector>();
        services.AddSingleton<IAnomalyDetector, MlNetAnomalyDetector>();

        services.AddTransient<HomeViewModel>();
        services.AddTransient<DiagnoseViewModel>();
        services.AddTransient<ScanViewModel>();
        services.AddTransient<FindingsViewModel>();
        services.AddTransient<GuideViewModel>();
        services.AddTransient<MonitorViewModel>();
        services.AddTransient<FpsViewModel>();
        services.AddTransient<OptimizeViewModel>();
        services.AddTransient<AlertsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
