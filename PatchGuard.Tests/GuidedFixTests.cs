using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Models;
using PatchGuard.Services.Fixes;
using PatchGuard.Services.Optimization;

namespace PatchGuard.Tests;

public sealed class GuidedFixTests
{
    [Fact]
    public async Task Execute_WithoutConfirm_DoesNotRunSteps()
    {
        var step = new ControllableStep("Free up RAM (working sets)");
        await using var harness = await CreateServiceAsync([step]);

        var plan = RequirePlan(harness.Service.TryBuildFromFinding(CreateMemoryFinding()));
        var result = await harness.Service.ExecuteAsync(plan, GuidedFixConfirmation.Rejected);

        Assert.Equal(GuidedFixOutcome.RejectedNotConfirmed, result.Outcome);
        Assert.Equal(0, step.RunCount);
        Assert.Empty(result.StepResults);
        Assert.Empty(await harness.LoadRunsAsync());
    }

    [Fact]
    public async Task Execute_CancelMidRun_ReturnsCancelledAndPersists()
    {
        using var cts = new CancellationTokenSource();
        var first = new ControllableStep("First", onRun: () => cts.Cancel());
        var second = new ControllableStep("Second");
        await using var harness = await CreateServiceAsync([first, second]);

        var plan = new GuidedFixPlan
        {
            Id = "cancel-plan",
            Title = "Cancel test",
            Source = "Test",
            Steps =
            [
                CreateOptStep("First"),
                CreateOptStep("Second")
            ]
        };

        var result = await harness.Service.ExecuteAsync(
            plan,
            GuidedFixConfirmation.ConfirmNow(),
            cancellationToken: cts.Token);

        Assert.Equal(GuidedFixOutcome.Cancelled, result.Outcome);
        Assert.Equal(1, first.RunCount);
        Assert.Equal(0, second.RunCount);
        var record = Assert.Single(await harness.LoadRunsAsync());
        Assert.Equal(nameof(GuidedFixOutcome.Cancelled), record.Outcome);
    }

    [Fact]
    public async Task Execute_PartialFailure_ContinuesAndRecords()
    {
        var ok = new ControllableStep("Ok step");
        var bad = new ControllableStep("Bad step", fail: true);
        await using var harness = await CreateServiceAsync([ok, bad]);

        var plan = new GuidedFixPlan
        {
            Id = "partial-plan",
            Title = "Partial test",
            Source = "Test",
            Steps =
            [
                CreateOptStep("Ok step"),
                CreateOptStep("Bad step")
            ]
        };

        var result = await harness.Service.ExecuteAsync(plan, GuidedFixConfirmation.ConfirmNow());

        Assert.Equal(GuidedFixOutcome.PartialFailure, result.Outcome);
        Assert.False(result.Verified);
        Assert.Equal(1, ok.RunCount);
        Assert.Equal(1, bad.RunCount);
        Assert.Equal(2, result.StepResults.Count);
        var record = Assert.Single(await harness.LoadRunsAsync());
        Assert.Equal(nameof(GuidedFixOutcome.PartialFailure), record.Outcome);
        Assert.Equal(1, record.StepsSucceeded);
        Assert.Equal(2, record.StepsTotal);
    }

    [Fact]
    public void TryBuildFromFinding_BlocksAdminRequired()
    {
        var step = new ControllableStep("Free up RAM (working sets)");
        using var harness = CreateServiceSync([step]);

        var plan = harness.Service.TryBuildFromFinding(new Finding
        {
            ModuleName = "Memory",
            Title = "Needs admin",
            Details = "x",
            ActionState = FindingActionState.Recommended,
            AdminRequirement = FindingAdminRequirement.Required,
            Risk = FindingRisk.Medium
        });

        Assert.Null(plan);
    }

    [Fact]
    public void TryBuildFromFinding_MapsDiskSpaceToSafeActions()
    {
        var steps = new IOptimizationStep[]
        {
            new ControllableStep("Clear temporary files"),
            new ControllableStep("Empty Recycle Bin"),
            new ControllableStep("Restart Windows Explorer", optional: true)
        };
        using var harness = CreateServiceSync(steps);

        var plan = RequirePlan(harness.Service.TryBuildFromFinding(new Finding
        {
            ModuleName = "Disk space",
            Title = "Low disk",
            Details = "x",
            ActionState = FindingActionState.Recommended,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = FindingRisk.Low
        }));

        Assert.Contains(plan.Steps, s => s.OptimizationStepName == "Clear temporary files");
        Assert.Contains(plan.Steps, s => s.OptimizationStepName == "Empty Recycle Bin");
        Assert.Contains(plan.Steps, s => s.Kind == GuidedFixActionKind.LaunchUri);
        Assert.DoesNotContain(plan.Steps, s => s.OptimizationStepName == "Restart Windows Explorer");
    }

    [Fact]
    public async Task Execute_LaunchUri_UsesPolicyAndDoesNotRunWithoutConfirm()
    {
        var launched = new List<string>();
        await using var harness = await CreateServiceAsync(
            [],
            launchUri: uri =>
            {
                launched.Add(uri);
                return true;
            });

        var plan = RequirePlan(harness.Service.TryBuildFromFixStep(new FixStep
        {
            Order = 1,
            Title = "Open Storage Sense",
            Instructions = "Free up space",
            LinkUrl = "ms-settings:storagesense"
        }));

        var rejected = await harness.Service.ExecuteAsync(plan, GuidedFixConfirmation.Rejected);
        Assert.Equal(GuidedFixOutcome.RejectedNotConfirmed, rejected.Outcome);
        Assert.Empty(launched);

        var result = await harness.Service.ExecuteAsync(plan, GuidedFixConfirmation.ConfirmNow());
        Assert.Equal(GuidedFixOutcome.Succeeded, result.Outcome);
        Assert.True(result.Verified);
        Assert.Contains(launched, u => u.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase));
    }

    private static Finding CreateMemoryFinding() =>
        new()
        {
            ModuleName = "Memory",
            Title = "High RAM",
            Details = "Almost full",
            ActionState = FindingActionState.Recommended,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = FindingRisk.Low
        };

    private static GuidedFixPlanStep CreateOptStep(string name) =>
        new()
        {
            Id = name,
            Title = name,
            Description = name,
            Kind = GuidedFixActionKind.OptimizationStep,
            OptimizationStepName = name,
            Risk = FindingRisk.Low,
            AdminRequirement = FindingAdminRequirement.NotRequired
        };

    private static GuidedFixPlan RequirePlan(GuidedFixPlan? plan)
    {
        Assert.NotNull(plan);
        return plan;
    }

    private static async Task<Harness> CreateServiceAsync(
        IReadOnlyList<IOptimizationStep> steps,
        Func<string, bool>? launchUri = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var db = new PatchGuardDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
        }

        var factory = new TestDbContextFactory(options);
        var service = new GuidedFixPlanService(
            steps,
            factory,
            executionTimeout: TimeSpan.FromSeconds(5),
            launchUri: launchUri);
        return new Harness(connection, factory, service);
    }

    private static Harness CreateServiceSync(IReadOnlyList<IOptimizationStep> steps)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
            .UseSqlite(connection)
            .Options;
        using (var db = new PatchGuardDbContext(options))
        {
            db.Database.EnsureCreated();
        }

        var factory = new TestDbContextFactory(options);
        var service = new GuidedFixPlanService(steps, factory, TimeSpan.FromSeconds(5));
        return new Harness(connection, factory, service);
    }

    private sealed class Harness(
        SqliteConnection connection,
        TestDbContextFactory factory,
        GuidedFixPlanService service) : IAsyncDisposable, IDisposable
    {
        public GuidedFixPlanService Service { get; } = service;

        public async Task<IReadOnlyList<Data.Entities.GuidedFixRunRecord>> LoadRunsAsync()
        {
            await using var db = factory.CreateDbContext();
            return await db.GuidedFixRuns.AsNoTracking().ToListAsync();
        }

        public void Dispose() => connection.Dispose();

        public ValueTask DisposeAsync()
        {
            connection.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ControllableStep(
        string name,
        bool fail = false,
        bool optional = false,
        Action? onRun = null) : IOptimizationStep
    {
        public string Name { get; } = name;
        public string Description => Name;
        public bool IsOptional { get; } = optional;
        public int RunCount { get; private set; }

        public Task<OptimizationStepResult> RunAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunCount++;
            onRun?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new OptimizationStepResult
            {
                StepName = Name,
                Status = fail ? OptimizationStatus.Failed : OptimizationStatus.Success,
                Detail = fail ? "forced failure" : "ok",
                BytesFreed = fail ? 0 : 1024
            });
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<PatchGuardDbContext> options) : IDbContextFactory<PatchGuardDbContext>
    {
        public PatchGuardDbContext CreateDbContext() => new(options);
    }
}
