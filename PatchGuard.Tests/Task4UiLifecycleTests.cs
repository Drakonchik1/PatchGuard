using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Windows.Threading;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services;
using PatchGuard.Services.Alerts;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.Fixes;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.History;
using PatchGuard.Services.Health;
using PatchGuard.Services.Ml;
using PatchGuard.Services.Navigation;
using PatchGuard.Services.Optimization;
using PatchGuard.Services.Performance;
using PatchGuard.Services.Platform;
using PatchGuard.ViewModels;

namespace PatchGuard.Tests;

public sealed class Task4UiLifecycleTests
{
    [Fact]
    public async Task MonitorCapturesHardwareAwayFromCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var hardware = new RecordingHardwareService();
        var viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());

        viewModel.OnNavigatedTo();
        var captureThread = await hardware.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();

        Assert.NotEqual(callerThread, captureThread);
    }

    [Fact]
    public async Task LeavingMonitorCancelsInFlightPersistence()
    {
        var history = new CancellingSensorHistoryService();
        var viewModel = CreateMonitor(new RecordingHardwareService(), history);

        viewModel.OnNavigatedTo();
        await history.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();

        await history.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await history.RegistrationSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task TransientMonitorViewModelsSerializeCaptureProcessWide()
    {
        using var hardware = new BlockingHardwareService();
        var first = CreateMonitor(hardware, new NoOpSensorHistoryService());
        var second = CreateMonitor(hardware, new NoOpSensorHistoryService());

        first.OnNavigatedTo();
        await hardware.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        first.OnNavigatedFrom();
        second.OnNavigatedTo();

        await Task.Delay(100);
        var maxBeforeRelease = hardware.MaxActive;
        hardware.ReleaseFirst.Set();
        await hardware.SecondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        second.OnNavigatedFrom();
        Assert.Equal(1, maxBeforeRelease);
        Assert.Equal(1, hardware.MaxActive);
    }

    [Fact]
    public async Task MonitorReentryWaitsForPreviousCapture()
    {
        using var hardware = new BlockingHardwareService();
        var viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());

        viewModel.OnNavigatedTo();
        await hardware.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();
        viewModel.OnNavigatedTo();

        await Task.Delay(100);
        var maxBeforeRelease = hardware.MaxActive;
        hardware.ReleaseFirst.Set();
        await hardware.SecondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();
        Assert.Equal(1, maxBeforeRelease);
        Assert.Equal(1, hardware.MaxActive);
    }

    [Fact]
    public async Task TransientMonitorViewModelsSerializePersistenceProcessWide()
    {
        var history = new BlockingSensorHistoryService();
        var first = CreateMonitor(new RecordingHardwareService(), history);
        var second = CreateMonitor(new RecordingHardwareService(), history);

        first.OnNavigatedTo();
        await history.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        first.OnNavigatedFrom();
        second.OnNavigatedTo();

        await Task.Delay(100);
        var maxBeforeRelease = history.MaxActive;
        history.ReleaseFirst.TrySetResult();
        await history.SecondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        second.OnNavigatedFrom();
        Assert.Equal(1, maxBeforeRelease);
        Assert.Equal(1, history.MaxActive);
    }

    [Fact]
    public async Task MonitorWithoutOwningDispatcherSkipsObservableUpdates()
    {
        var hardware = new RecordingHardwareService(new HardwareSnapshot
        {
            CpuName = "Worker update must be skipped",
            CpuLoadPercent = 75
        });
        var viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());

        viewModel.OnNavigatedTo();
        await hardware.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        viewModel.OnNavigatedFrom();

        Assert.Equal("CPU", viewModel.CpuName);
        Assert.Empty(viewModel.Sensors);
    }

    [Fact]
    public async Task MonitorDoesNotUseUnrelatedCurrentDispatcher()
    {
        var hardware = new RecordingHardwareService(new HardwareSnapshot
        {
            CpuName = "Unrelated dispatcher update",
            CpuLoadPercent = 75
        });
        var viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());
        using var ready = new ManualResetEventSlim();
        Dispatcher? unrelatedDispatcher = null;
        var thread = new Thread(() =>
        {
            unrelatedDispatcher = Dispatcher.CurrentDispatcher;
            viewModel.OnNavigatedTo();
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        await hardware.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        await unrelatedDispatcher!.InvokeAsync(viewModel.OnNavigatedFrom);
        unrelatedDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));

        Assert.Equal("CPU", viewModel.CpuName);
    }

    [Fact]
    public async Task MonitorIgnoresCaptureCompletedAfterNavigation()
    {
        using var hardware = new BlockingHardwareService(
            new HardwareSnapshot { CpuName = "Stale CPU" });
        MonitorViewModel? viewModel = null;
        StaTestHost.Run(() =>
        {
            viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());
            viewModel.OnNavigatedTo();
        });
        await hardware.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        StaTestHost.Run(viewModel!.OnNavigatedFrom);
        hardware.ReleaseFirst.Set();
        await hardware.FirstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        StaTestHost.Run(() => Assert.Equal("CPU", viewModel!.CpuName));
    }

    [Fact]
    public async Task MonitorContinuesAfterTransientCaptureFailure()
    {
        var hardware = new TransientFailureHardwareService();
        var viewModel = CreateMonitor(hardware, new NoOpSensorHistoryService());

        viewModel.OnNavigatedTo();
        await hardware.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        viewModel.OnNavigatedFrom();

        Assert.True(hardware.CaptureCount >= 2);
    }

    [Fact]
    public async Task DiagnosticsRunBlockingModulesAwayFromCallingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var module = new RecordingDiagnosticModule();
        var orchestrator = new DiagnosticOrchestrator([module]);

        await orchestrator.RunScanAsync(
            ScanScenario.FullSystemAudit,
            new Progress<DiagnosticProgressItem>());

        Assert.NotEqual(callerThread, module.RunThreadId);
    }

    [Fact]
    public async Task OptimizerRunsBlockingStepsAwayFromCallingThreadAndSequentially()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var concurrency = new StepConcurrency();
        var first = new RecordingOptimizationStep("First", concurrency);
        var second = new RecordingOptimizationStep("Second", concurrency);
        var optimizer = new SystemOptimizerService([first, second]);

        var summary = await optimizer.RunAsync(
            includeOptional: true,
            new Progress<OptimizationStepResult>());

        Assert.All([first, second], step => Assert.NotEqual(callerThread, step.RunThreadId));
        Assert.Equal(1, concurrency.MaxActive);
        Assert.Equal(["First", "Second"], summary.Steps.Select(step => step.StepName));
    }

    [Fact]
    public async Task LeavingFpsCancelsCapture()
    {
        var capture = new CancellingFpsCaptureService();
        var viewModel = new FpsViewModel(
            capture,
            new NoOpPerformanceHistoryService(),
            new NoOpElevationService());
        viewModel.OnNavigatedTo();

        viewModel.CaptureCommand.Execute(null);
        await capture.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var leave = Assert.IsAssignableFrom<INavigationLeave>(viewModel);
        leave.OnNavigatedFrom();

        await capture.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await capture.RegistrationSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task LeavingOptimizeCancelsRun()
    {
        var optimizer = new CancellingOptimizerService();
        var viewModel = new OptimizeViewModel(optimizer, new NoOpPerformanceHistoryService());
        viewModel.OnNavigatedTo();

        viewModel.OptimizeCommand.Execute(null);
        await optimizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var leave = Assert.IsAssignableFrom<INavigationLeave>(viewModel);
        leave.OnNavigatedFrom();

        await optimizer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await optimizer.RegistrationSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task LeavingFindingsCancelsGuidedFix()
    {
        var fixes = new CancellingGuidedFixService();
        var session = new ScanSessionState();
        session.Findings.Add(CreateFinding());
        var viewModel = new FindingsViewModel(
            new NoOpNavigationService(),
            session,
            new HealthScorePolicy(),
            fixes,
            new StubUserConfirmationService(confirm: true));
        viewModel.OnNavigatedTo();

        viewModel.RunSafeFixCommand.Execute(Assert.Single(viewModel.Findings));
        await fixes.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var leave = Assert.IsAssignableFrom<INavigationLeave>(viewModel);
        leave.OnNavigatedFrom();

        await fixes.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await fixes.RegistrationSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task LeavingGuideCancelsGuidedFix()
    {
        var fixes = new CancellingGuidedFixService();
        var session = new ScanSessionState
        {
            SelectedScenario = ScanScenario.QuickHealthCheck,
            Guide = new RepairGuide
            {
                Summary = "Ready",
                ChiefVerdict = "Ready",
                Steps =
                [
                    new FixStep
                    {
                        Order = 1,
                        Title = "Safe step",
                        Instructions = "Run it"
                    }
                ]
            }
        };
        var viewModel = new GuideViewModel(
            new NoOpNavigationService(),
            session,
            new NoOpCouncilService(),
            fixes,
            new StubUserConfirmationService(confirm: true));
        viewModel.OnNavigatedTo();

        viewModel.RunSafeFixCommand.Execute(Assert.Single(viewModel.FixSteps));
        await fixes.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.OnNavigatedFrom();

        await fixes.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(await fixes.RegistrationSucceeded.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void FindingsConfirmationExceptionDoesNotLeakLifecycleLease()
    {
        var fixes = new CancellingGuidedFixService();
        var session = new ScanSessionState();
        session.Findings.Add(CreateFinding());
        var viewModel = new FindingsViewModel(
            new NoOpNavigationService(),
            session,
            new HealthScorePolicy(),
            fixes,
            new ThrowingConfirmationService());
        viewModel.OnNavigatedTo();

        _ = Record.Exception(() =>
            viewModel.RunSafeFixCommand.Execute(Assert.Single(viewModel.Findings)));

        Assert.Equal(0, GetActiveLifecycleConsumers(viewModel));
        viewModel.OnNavigatedFrom();
    }

    [Fact]
    public void GuideConfirmationExceptionDoesNotLeakLifecycleLease()
    {
        var session = new ScanSessionState
        {
            SelectedScenario = ScanScenario.QuickHealthCheck,
            Guide = new RepairGuide
            {
                Summary = "Ready",
                ChiefVerdict = "Ready",
                Steps =
                [
                    new FixStep
                    {
                        Order = 1,
                        Title = "Safe step",
                        Instructions = "Run it"
                    }
                ]
            }
        };
        var viewModel = new GuideViewModel(
            new NoOpNavigationService(),
            session,
            new NoOpCouncilService(),
            new CancellingGuidedFixService(),
            new ThrowingConfirmationService());
        viewModel.OnNavigatedTo();

        _ = Record.Exception(() =>
            viewModel.RunSafeFixCommand.Execute(Assert.Single(viewModel.FixSteps)));

        Assert.Equal(0, GetActiveLifecycleConsumers(viewModel));
        viewModel.OnNavigatedFrom();
    }

    [Fact]
    public void OptimizePreviewExceptionDoesNotLeakLifecycleLease()
    {
        var optimizer = new ThrowingPreviewOptimizerService();
        var viewModel = new OptimizeViewModel(
            optimizer,
            new NoOpPerformanceHistoryService());
        viewModel.OnNavigatedTo();

        _ = Record.Exception(() => viewModel.OptimizeCommand.Execute(null));

        Assert.Equal(0, GetActiveLifecycleConsumers(viewModel));
        viewModel.OnNavigatedFrom();
    }

    [Fact]
    public void JourneyNavigationRetainsBackPath()
    {
        using var provider = CreateNavigationProvider();
        var host = new TestHost { CurrentViewModel = new JourneyStart() };
        var navigation = new NavigationService(provider, host);

        navigation.NavigateTo<JourneyMiddle>();
        navigation.NavigateTo<JourneyEnd>();
        navigation.GoBack();

        Assert.IsType<JourneyMiddle>(host.CurrentViewModel);
        Assert.True(navigation.CanGoBack);
    }

    [Fact]
    public void TopLevelNavigationClearsTransientJourneyHistory()
    {
        using var provider = CreateNavigationProvider();
        var host = new TestHost { CurrentViewModel = new JourneyStart() };
        var navigation = new NavigationService(provider, host);
        navigation.NavigateTo<JourneyMiddle>();
        navigation.NavigateTo<JourneyEnd>();
        var topLevelMethod = typeof(INavigationService)
            .GetMethods()
            .SingleOrDefault(method => method.Name == "NavigateTopLevel");

        Assert.NotNull(topLevelMethod);
        topLevelMethod.MakeGenericMethod(typeof(TopLevelDestination))
            .Invoke(navigation, null);

        Assert.IsType<TopLevelDestination>(host.CurrentViewModel);
        Assert.False(navigation.CanGoBack);
    }

    private static MonitorViewModel CreateMonitor(
        IHardwareMonitorService hardware,
        ISensorHistoryService history) =>
        new(
            hardware,
            new NoOpElevationService(),
            history,
            new NoOpAlertRuleEngine(),
            new NoOpAnomalyDetector());

    private static ServiceProvider CreateNavigationProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<JourneyMiddle>();
        services.AddTransient<JourneyEnd>();
        services.AddTransient<TopLevelDestination>();
        return services.BuildServiceProvider();
    }

    private static Finding CreateFinding() =>
        new()
        {
            ModuleName = "Memory",
            Title = "High RAM",
            Details = "Measured",
            ActionState = FindingActionState.Recommended,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = FindingRisk.Low
        };

    private static int GetActiveLifecycleConsumers(object viewModel)
    {
        var lifecycle = viewModel.GetType()
            .GetField("_lifecycle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel);
        Assert.NotNull(lifecycle);
        return (int)lifecycle.GetType()
            .GetField("_activeConsumers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(lifecycle)!;
    }

    private sealed class RecordingHardwareService(
        HardwareSnapshot? snapshot = null) : IHardwareMonitorService
    {
        public TaskCompletionSource<int> Captured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HardwareSnapshot Capture()
        {
            Captured.TrySetResult(Environment.CurrentManagedThreadId);
            return snapshot ?? new HardwareSnapshot { CpuLoadPercent = 25 };
        }

        public void Dispose()
        {
        }
    }

    private sealed class BlockingHardwareService(
        HardwareSnapshot? snapshot = null) : IHardwareMonitorService
    {
        private int _active;
        private int _captureCount;
        private int _maxActive;

        public ManualResetEventSlim ReleaseFirst { get; } = new(false);
        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxActive => Volatile.Read(ref _maxActive);

        public HardwareSnapshot Capture()
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(ref _maxActive, active);
            var capture = Interlocked.Increment(ref _captureCount);
            try
            {
                if (capture == 1)
                {
                    FirstStarted.TrySetResult();
                    ReleaseFirst.Wait();
                    FirstCompleted.TrySetResult();
                }
                else if (capture == 2)
                {
                    SecondCompleted.TrySetResult();
                }

                return snapshot ?? new HardwareSnapshot { CpuLoadPercent = 25 };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose() => ReleaseFirst.Dispose();
    }

    private sealed class TransientFailureHardwareService : IHardwareMonitorService
    {
        private int _captureCount;
        public int CaptureCount => Volatile.Read(ref _captureCount);
        public TaskCompletionSource Recovered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HardwareSnapshot Capture()
        {
            if (Interlocked.Increment(ref _captureCount) == 1)
            {
                throw new InvalidOperationException("Transient capture failure");
            }

            Recovered.TrySetResult();
            return new HardwareSnapshot { CpuLoadPercent = 25 };
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoOpSensorHistoryService : ISensorHistoryService
    {
        public Task SaveSnapshotAsync(
            HardwareSnapshot snapshot,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorSnapshotRecord>>([]);

        public Task<SensorSnapshotRecord?> GetLatestAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SensorSnapshotRecord?>(null);
    }

    private sealed class CancellingSensorHistoryService : ISensorHistoryService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RegistrationSucceeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SaveSnapshotAsync(
            HardwareSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                try
                {
                    Assert.True(cancellationToken.WaitHandle.WaitOne(0));
                    using var registration = cancellationToken.Register(static () => { });
                    RegistrationSucceeded.TrySetResult(true);
                }
                catch (ObjectDisposedException)
                {
                    RegistrationSucceeded.TrySetResult(false);
                }

                throw;
            }
        }

        public Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorSnapshotRecord>>([]);

        public Task<SensorSnapshotRecord?> GetLatestAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SensorSnapshotRecord?>(null);
    }

    private sealed class BlockingSensorHistoryService : ISensorHistoryService
    {
        private int _active;
        private int _saveCount;
        private int _maxActive;

        public TaskCompletionSource FirstStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task SaveSnapshotAsync(
            HardwareSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMax(ref _maxActive, active);
            var save = Interlocked.Increment(ref _saveCount);
            try
            {
                if (save == 1)
                {
                    FirstStarted.TrySetResult();
                    await ReleaseFirst.Task;
                }
                else if (save == 2)
                {
                    SecondCompleted.TrySetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorSnapshotRecord>>([]);

        public Task<SensorSnapshotRecord?> GetLatestAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SensorSnapshotRecord?>(null);
    }

    private sealed class NoOpAlertRuleEngine : IAlertRuleEngine
    {
        public IReadOnlyList<Alert> Evaluate(HardwareSnapshot snapshot) => [];
        public IReadOnlyList<Alert> Evaluate(SensorSnapshotRecord snapshot) => [];
    }

    private sealed class NoOpAnomalyDetector : IAnomalyDetector
    {
        public string Name => "None";
        public bool IsAvailable => true;
        public IReadOnlyList<AnomalyHit> Detect(IReadOnlyList<SensorSnapshotRecord> history) => [];
    }

    private sealed class NoOpElevationService : IAdminElevationService
    {
        public bool IsElevated => false;
        public bool RestartElevated() => false;
    }

    private sealed class RecordingDiagnosticModule : IDiagnosticModule
    {
        public string Name => "Blocking";
        public string Description => "Records its thread";
        public bool IsImplemented => true;
        public int RunThreadId { get; private set; }

        public Task<IReadOnlyList<Finding>> RunAsync(
            CancellationToken cancellationToken = default)
        {
            RunThreadId = Environment.CurrentManagedThreadId;
            return Task.FromResult<IReadOnlyList<Finding>>([]);
        }
    }

    private sealed class StepConcurrency
    {
        private int _active;
        public int MaxActive { get; private set; }

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
        }

        public void Exit() => Interlocked.Decrement(ref _active);
    }

    private sealed class RecordingOptimizationStep(
        string name,
        StepConcurrency concurrency) : IOptimizationStep
    {
        public string Name { get; } = name;
        public string Description => Name;
        public bool IsOptional => false;
        public int RunThreadId { get; private set; }

        public Task<OptimizationStepResult> RunAsync(
            CancellationToken cancellationToken = default)
        {
            RunThreadId = Environment.CurrentManagedThreadId;
            concurrency.Enter();
            try
            {
                Thread.Sleep(20);
                return Task.FromResult(new OptimizationStepResult
                {
                    StepName = Name,
                    Status = OptimizationStatus.Success
                });
            }
            finally
            {
                concurrency.Exit();
            }
        }
    }

    private sealed class CancellingFpsCaptureService : IFpsCaptureService
    {
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RegistrationSucceeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<GameProcessInfo> GetCandidateProcesses() =>
        [
            new GameProcessInfo { ProcessId = 1, ProcessName = "Game" }
        ];

        public async Task<FpsCaptureResult> CaptureAsync(
            GameProcessInfo target,
            int seconds,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                try
                {
                    Assert.True(cancellationToken.WaitHandle.WaitOne(0));
                    using var registration = cancellationToken.Register(static () => { });
                    RegistrationSucceeded.TrySetResult(true);
                }
                catch (ObjectDisposedException)
                {
                    RegistrationSucceeded.TrySetResult(false);
                }

                throw;
            }

            return FpsCaptureResult.Failed(target.ProcessName, "unreachable");
        }
    }

    private sealed class CancellingOptimizerService : ISystemOptimizerService
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RegistrationSucceeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<IOptimizationStep> GetSteps(bool includeOptional) => [];

        public async Task<OptimizationRunSummary> RunAsync(
            bool includeOptional,
            IProgress<OptimizationStepResult> progress,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                try
                {
                    Assert.True(cancellationToken.WaitHandle.WaitOne(0));
                    using var registration = cancellationToken.Register(static () => { });
                    RegistrationSucceeded.TrySetResult(true);
                }
                catch (ObjectDisposedException)
                {
                    RegistrationSucceeded.TrySetResult(false);
                }

                throw;
            }

            return new OptimizationRunSummary();
        }
    }

    private sealed class ThrowingPreviewOptimizerService : ISystemOptimizerService
    {
        private int _getStepsCalls;

        public IReadOnlyList<IOptimizationStep> GetSteps(bool includeOptional)
        {
            if (Interlocked.Increment(ref _getStepsCalls) > 1)
            {
                throw new InvalidOperationException("Preview failed");
            }

            return [];
        }

        public Task<OptimizationRunSummary> RunAsync(
            bool includeOptional,
            IProgress<OptimizationStepResult> progress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OptimizationRunSummary());
    }

    private sealed class NoOpPerformanceHistoryService : IPerformanceHistoryService
    {
        public Task SaveFpsAsync(
            FpsCaptureResult result,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<FpsCaptureRecord>> GetRecentFpsAsync(
            int take = 8,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FpsCaptureRecord>>([]);

        public Task SaveOptimizationAsync(
            OptimizationRunSummary summary,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OptimizationRunRecord>> GetRecentOptimizationsAsync(
            int take = 8,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OptimizationRunRecord>>([]);
    }

    private sealed class CancellingGuidedFixService : IGuidedFixPlanService
    {
        private readonly GuidedFixPlan _plan = new()
        {
            Id = "test",
            Title = "Test",
            Source = "Test",
            Steps =
            [
                new GuidedFixPlanStep
                {
                    Id = "safe",
                    Title = "Safe",
                    Description = "Safe",
                    Kind = GuidedFixActionKind.OptimizationStep,
                    Risk = FindingRisk.Low,
                    AdminRequirement = FindingAdminRequirement.NotRequired
                }
            ]
        };

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> RegistrationSucceeded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GuidedFixPlan? TryBuildFromFinding(
            Finding finding,
            string? linkedScanScenario = null) => _plan;

        public GuidedFixPlan? TryBuildFromFixStep(
            FixStep step,
            string? linkedScanScenario = null) => _plan;

        public GuidedFixPreview Preview(GuidedFixPlan plan) =>
            new()
            {
                Plan = plan,
                Summary = "Ready",
                StepSummaries = ["Safe"],
                Risk = FindingRisk.Low,
                AdminRequirement = FindingAdminRequirement.NotRequired
            };

        public async Task<GuidedFixRunResult> ExecuteAsync(
            GuidedFixPlan plan,
            GuidedFixConfirmation confirmation,
            IProgress<OptimizationStepResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                try
                {
                    Assert.True(cancellationToken.WaitHandle.WaitOne(0));
                    using var registration = cancellationToken.Register(static () => { });
                    RegistrationSucceeded.TrySetResult(true);
                }
                catch (ObjectDisposedException)
                {
                    RegistrationSucceeded.TrySetResult(false);
                }

                throw;
            }

            return new GuidedFixRunResult
            {
                Outcome = GuidedFixOutcome.Succeeded,
                Summary = "unreachable"
            };
        }
    }

    private sealed class NoOpCouncilService : Services.Ai.IAiCouncilService
    {
        public Task<RepairGuide> BuildGuideAsync(
            ScanScenario scenario,
            IReadOnlyList<Finding> findings,
            IProgress<CouncilProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default,
            bool allowExternalServices = false) =>
            Task.FromResult(new RepairGuide { Summary = "Ready", ChiefVerdict = "Ready" });
    }

    private sealed class NoOpNavigationService : INavigationService
    {
        public bool CanGoBack => false;
        public void NavigateTo<TViewModel>() where TViewModel : class { }
        public void NavigateHome() { }
        public void GoBack() { }
    }

    private sealed class ThrowingConfirmationService : IUserConfirmationService
    {
        public bool Confirm(string title, string message) =>
            throw new InvalidOperationException("Confirmation failed");
    }

    private sealed class TestHost : IViewModelHost
    {
        public object? CurrentViewModel { get; set; }
    }

    private sealed class JourneyStart;
    private sealed class JourneyMiddle;
    private sealed class JourneyEnd;
    private sealed class TopLevelDestination;

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current ||
                Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
