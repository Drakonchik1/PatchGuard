using System.IO;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Models;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class CouncilEvaluationServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsAggregateMetricsOnly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new CouncilEvaluationService(database.Factory, new CouncilEvaluator());

        var guide = new RepairGuide
        {
            Summary = "Council prepared a safe recovery order.",
            ChiefVerdict = "Restart the affected services and rescan.",
            Sources = [GuidanceSource.Local, GuidanceSource.AiGenerated, GuidanceSource.WebSourced],
            CouncilDiscussion =
            [
                new CouncilMessage
                {
                    AgentRole = "Technician",
                    Content = "User alice profile path C:\\Users\\alice should not be stored anywhere else.",
                    Headline = "Repair first",
                    Phase = CouncilPhaseType.Analysis,
                    Round = 1,
                    Confidence = 74
                }
            ],
            WebReferences =
            [
                new WebReference
                {
                    Title = "Vendor guidance",
                    Url = "https://support.example.com/fix",
                    Domain = "support.example.com",
                    UsedFor = "Update service repair"
                }
            ],
            Steps =
            [
                new FixStep
                {
                    Order = 1,
                    Title = "Restart services",
                    Instructions = "Restart the affected Windows Update services and confirm both return to Running state.",
                    CopyText = "services.msc"
                }
            ]
        };

        await service.SaveAsync(ScanScenario.AfterWindowsUpdate, guide, TimeSpan.FromMilliseconds(321));

        await using var dbContext = await database.Factory.CreateDbContextAsync();
        var record = Assert.Single(await dbContext.CouncilEvaluations.ToListAsync());
        Assert.Equal("AfterWindowsUpdate", record.Scenario);
        Assert.Equal("AI+Web", record.Source);
        Assert.Equal(321, record.LatencyMs);
        Assert.Equal(1, record.FixStepCount);
        Assert.Equal(1, record.CouncilMessageCount);
        Assert.Equal(100, record.ActionabilityScore);
        Assert.Equal(100, record.ConsistencyScore);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path;

        private TestDatabase(string path, TestDbContextFactory factory)
        {
            _path = path;
            Factory = factory;
        }

        public TestDbContextFactory Factory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"patchguard-eval-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .Options;
            var factory = new TestDbContextFactory(options);
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(path, factory);
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TestDbContextFactory(
        DbContextOptions<PatchGuardDbContext> options) : IDbContextFactory<PatchGuardDbContext>
    {
        public PatchGuardDbContext CreateDbContext() => new(options);

        public Task<PatchGuardDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
