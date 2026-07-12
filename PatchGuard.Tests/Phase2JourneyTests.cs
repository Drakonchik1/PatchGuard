using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using PatchGuard.Data;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.Health;
using PatchGuard.Services.History;
using PatchGuard.Services.Navigation;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class Phase2JourneyTests
{
    [Fact]
    public void HealthScorePolicyDefinesStableBoundaries()
    {
        var policyType = typeof(FindingsViewModel).Assembly
            .GetType("PatchGuard.Services.Health.HealthScorePolicy");

        Assert.NotNull(policyType);
        var policy = Activator.CreateInstance(policyType);
        var calculate = policyType.GetMethod("Calculate");
        Assert.NotNull(calculate);

        Assert.Equal(100, calculate.Invoke(policy, [Array.Empty<Finding>()]));
        Assert.Equal(88, calculate.Invoke(policy, [new[] { CreateFinding(FindingSeverity.Warning) }]));
        Assert.Equal(70, calculate.Invoke(policy, [Enumerable.Repeat(CreateFinding(FindingSeverity.Critical), 8).ToArray()]));
    }

    [Fact]
    public void CurrentAndHistoryScoringDependOnSamePolicy()
    {
        var policyType = typeof(FindingsViewModel).Assembly
            .GetType("PatchGuard.Services.Health.IHealthScorePolicy");

        Assert.NotNull(policyType);
        Assert.Contains(policyType, ConstructorParameterTypes(typeof(FindingsViewModel)));
        Assert.Contains(policyType, ConstructorParameterTypes(typeof(ScanHistoryService)));
    }

    [Fact]
    public async Task CurrentAndHistoryScoringProduceSameResult()
    {
        var findings = new[]
        {
            CreateFinding(FindingSeverity.Warning),
            CreateFinding(FindingSeverity.Critical)
        };
        var policy = new HealthScorePolicy();
        var session = new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck };
        session.Findings.AddRange(findings);
        var current = new FindingsViewModel(new RecordingNavigationService(), session, policy);
        current.OnNavigatedTo();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var dbContext = new PatchGuardDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        var history = new ScanHistoryService(new TestDbContextFactory(options), policy);
        await history.SaveScanAsync(ScanScenario.QuickHealthCheck, findings);

        var saved = Assert.Single(await history.GetRecentScansAsync());
        Assert.Equal(current.HealthScore, saved.HealthScore);
    }

    [Fact]
    public async Task NavigatingToGuideDoesNotRunCouncil()
    {
        var council = new RecordingCouncilService();
        var viewModel = new GuideViewModel(
            new RecordingNavigationService(),
            new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck },
            council);

        viewModel.OnNavigatedTo();
        await Task.Delay(50);

        Assert.Equal(0, council.CallCount);
        Assert.False(viewModel.IsCouncilRunning);
    }

    [Fact]
    public async Task LeavingGuideCancelsRunningCouncil()
    {
        var council = new RecordingCouncilService { WaitForCancellation = true };
        var viewModel = new GuideViewModel(
            new RecordingNavigationService(),
            new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck },
            council);

        viewModel.RunCouncilCommand.Execute(null);
        await council.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();
        await council.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsCouncilRunning);
    }

    [Fact]
    public async Task CancelledGuideIgnoresLateCouncilResult()
    {
        var council = new RecordingCouncilService { WaitForRelease = true };
        var session = new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck };
        var viewModel = new GuideViewModel(new RecordingNavigationService(), session, council);

        viewModel.RunCouncilCommand.Execute(null);
        await council.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();
        council.Release.TrySetResult();
        await council.Returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        Assert.Null(session.Guide);
    }

    [Fact]
    public void ExistingGuideLabelsOnlySourcesThatWereUsed()
    {
        var session = new ScanSessionState
        {
            SelectedScenario = ScanScenario.QuickHealthCheck,
            Guide = new RepairGuide
            {
                Summary = "Test",
                ChiefVerdict = "Test",
                Sources = [GuidanceSource.Local, GuidanceSource.AiGenerated]
            }
        };
        var viewModel = new GuideViewModel(
            new RecordingNavigationService(),
            session,
            new RecordingCouncilService());

        viewModel.OnNavigatedTo();

        Assert.Equal(["Local diagnostic data", "AI-generated advice"], viewModel.SourceLabels);
        Assert.DoesNotContain("Web-sourced research", viewModel.SourceLabels);
    }

    [Fact]
    public async Task CancellingScanReturnsToScenarioChoice()
    {
        var navigation = new RecordingNavigationService();
        var orchestrator = new CancellableOrchestrator();
        var viewModel = new ScanViewModel(
            navigation,
            new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck },
            orchestrator,
            new NoOpHistoryService());

        viewModel.OnNavigatedTo();
        await orchestrator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelScanCommand.Execute(null);
        await orchestrator.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(typeof(DiagnoseViewModel), navigation.DestinationType);
        Assert.True(orchestrator.WasCancelled);
    }

    [Fact]
    public async Task CancelledScanIgnoresLateHistoryCompletion()
    {
        var navigation = new RecordingNavigationService();
        var history = new ControlledHistoryService();
        var viewModel = new ScanViewModel(
            navigation,
            new ScanSessionState { SelectedScenario = ScanScenario.QuickHealthCheck },
            new ImmediateOrchestrator(),
            history);

        viewModel.OnNavigatedTo();
        await history.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CancelScanCommand.Execute(null);
        history.Release.TrySetResult();
        await history.Returned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(20);

        Assert.Equal(typeof(DiagnoseViewModel), navigation.DestinationType);
    }

    [Fact]
    public void FindingUsesTypedActionAndVerificationMetadata()
    {
        var findingType = typeof(Finding);

        Assert.Equal("FindingAdminRequirement", findingType.GetProperty("AdminRequirement")?.PropertyType.Name);
        Assert.Equal("FindingRisk", findingType.GetProperty("Risk")?.PropertyType.Name);
        Assert.Equal("FindingVerificationStatus", findingType.GetProperty("VerificationStatus")?.PropertyType.Name);
        Assert.NotNull(findingType.GetProperty("Evidence"));
        Assert.NotNull(findingType.GetProperty("RecommendedFix"));
    }

    [Fact]
    public void FindingsBackReturnsToScanChoice()
    {
        var navigation = new RecordingNavigationService();
        var viewModel = new FindingsViewModel(
            navigation,
            new ScanSessionState(),
            new HealthScorePolicy());

        viewModel.GoBackCommand.Execute(null);

        Assert.Equal(typeof(DiagnoseViewModel), navigation.DestinationType);
    }

    [Fact]
    public void RepairGuideTracksOnlyActualAdviceSources()
    {
        var sourcesProperty = typeof(RepairGuide).GetProperty("Sources");

        Assert.NotNull(sourcesProperty);
        Assert.Contains(
            "GuidanceSource",
            sourcesProperty.PropertyType.ToString(),
            StringComparison.Ordinal);
    }

    private static Type[] ConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();

    private static Finding CreateFinding(FindingSeverity severity) =>
        new()
        {
            ModuleName = "Test",
            Title = "Test finding",
            Details = "Measured evidence",
            Severity = severity,
            Risk = FindingRisk.Medium
        };

    private sealed class RecordingCouncilService : IAiCouncilService
    {
        public int CallCount { get; private set; }
        public bool WaitForCancellation { get; init; }
        public bool WaitForRelease { get; init; }
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RepairGuide> BuildGuideAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            IProgress<CouncilProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default,
            bool allowExternalServices = false)
        {
            CallCount++;
            Started.TrySetResult();
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult();
                    throw;
                }
            }

            if (WaitForRelease)
            {
                await Release.Task;
            }

            Returned.TrySetResult();
            return new RepairGuide { Summary = "Test", ChiefVerdict = "Test" };
        }
    }

    private sealed class ImmediateOrchestrator : IDiagnosticOrchestrator
    {
        public IReadOnlyList<IDiagnosticModule> GetModulesForScenario(ScanScenario scenario) => [];

        public Task<IReadOnlyList<Finding>> RunScanAsync(
            ScanScenario scenario,
            IProgress<DiagnosticProgressItem> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Finding>>([]);
    }

    private sealed class ControlledHistoryService : IScanHistoryService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Returned { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SaveScanAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            Returned.TrySetResult();
        }

        public Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
            int take = 6,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanHistoryEntry>>([]);
    }

    private sealed class CancellableOrchestrator : IDiagnosticOrchestrator
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public IReadOnlyList<IDiagnosticModule> GetModulesForScenario(ScanScenario scenario) => [];

        public async Task<IReadOnlyList<Finding>> RunScanAsync(
            ScanScenario scenario,
            IProgress<DiagnosticProgressItem> progress,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                Cancelled.SetResult();
                throw;
            }

            return [];
        }
    }

    private sealed class NoOpHistoryService : IScanHistoryService
    {
        public Task SaveScanAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
            int take = 6,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanHistoryEntry>>([]);
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public Type? DestinationType { get; private set; }
        public bool CanGoBack => false;

        public void NavigateTo<TViewModel>() where TViewModel : class =>
            DestinationType = typeof(TViewModel);

        public void NavigateHome() => DestinationType = typeof(HomeViewModel);
        public void GoBack() => DestinationType = typeof(DiagnoseViewModel);
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<PatchGuardDbContext> options) : IDbContextFactory<PatchGuardDbContext>
    {
        public PatchGuardDbContext CreateDbContext() => new(options);
    }
}
