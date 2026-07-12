using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PatchGuard.Data;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.Health;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Optimization;
using PatchGuard.Services.Optimization.Steps;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;
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
            ApiKey = configuration[$"{AiOptions.OpenAiSection}:ApiKey"] ?? string.Empty,
            Model = configuration[$"{AiOptions.OpenAiSection}:Model"] ?? "gpt-4o-mini",
            WebSearchProvider = configuration[$"{AiOptions.WebSearchSection}:Provider"] ?? "tavily",
            WebSearchApiKey = configuration[$"{AiOptions.WebSearchSection}:ApiKey"] ?? string.Empty
        };

        services.AddSingleton(aiOptions);

        services.AddDbContextFactory<PatchGuardDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddHttpClient<OpenAiChatClient>();
        services.AddHttpClient<TavilyWebSearchService>();

        services.AddSingleton<ScanSessionState>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IViewModelHost>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddSingleton<INavigationService, NavigationService>();

        // Platform + hardware services
        services.AddSingleton<IAdminElevationService, AdminElevationService>();
        services.AddSingleton<IHardwareMonitorService, LibreHardwareMonitorService>();
        services.AddSingleton<IFpsCaptureService, PresentMonFpsCaptureService>();

        // Optimizer steps run in registration order.
        services.AddSingleton<IOptimizationStep, WorkingSetTrimStep>();
        services.AddSingleton<IOptimizationStep, TempFilesCleanStep>();
        services.AddSingleton<IOptimizationStep, RecycleBinStep>();
        services.AddSingleton<IOptimizationStep, DnsFlushStep>();
        services.AddSingleton<IOptimizationStep, ExplorerRestartStep>();
        services.AddSingleton<ISystemOptimizerService, SystemOptimizerService>();

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

        services.AddSingleton<IDiagnosticOrchestrator, DiagnosticOrchestrator>();
        services.AddSingleton<IHealthScorePolicy, HealthScorePolicy>();
        services.AddSingleton<DatabaseSchemaInitializer>();
        services.AddSingleton<IWebSearchService, TavilyWebSearchService>();
        services.AddSingleton<IAiCouncilService, AiCouncilService>();
        services.AddSingleton<IScanHistoryService, ScanHistoryService>();
        services.AddSingleton<IPerformanceHistoryService, PerformanceHistoryService>();

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
