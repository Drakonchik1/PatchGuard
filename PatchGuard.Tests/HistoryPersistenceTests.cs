using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Health;
using PatchGuard.Services.History;

namespace PatchGuard.Tests;

public sealed class HistoryPersistenceTests
{
    [Fact]
    public async Task ConcurrentHistoryOperationsUseIndependentContexts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var policy = new MutableHealthScorePolicy("test-v1", 90);
        var service = new ScanHistoryService(database.Factory, policy);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            service.SaveScanAsync(
                ScanScenario.QuickHealthCheck,
                [CreateFinding($"Finding {index}")])));

        var records = await service.GetRecentScansAsync(20);

        Assert.Equal(12, records.Count);
        Assert.True(database.Factory.CreationCount >= 13);
    }

    [Fact]
    public async Task SavedScoreDoesNotChangeWhenPolicyChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var policy = new MutableHealthScorePolicy("test-v1", 84);
        var service = new ScanHistoryService(database.Factory, policy);
        await service.SaveScanAsync(
            ScanScenario.QuickHealthCheck,
            [CreateFinding("Snapshot")]);

        policy.Score = 21;
        policy.Version = "test-v2";

        var saved = Assert.Single(await service.GetRecentScansAsync());
        Assert.Equal(84, saved.HealthScore);

        await using var context = await database.Factory.CreateDbContextAsync();
        var record = Assert.Single(await context.ScanRecords.ToListAsync());
        Assert.Equal(84, record.HealthScore);
        Assert.Equal("test-v1", record.ScorePolicyVersion);
    }

    [Fact]
    public async Task ConcurrentPerformanceOperationsUseIndependentContexts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new PerformanceHistoryService(database.Factory);

        await Task.WhenAll(Enumerable.Range(1, 10).Select(index =>
            service.SaveFpsAsync(new FpsCaptureResult
            {
                ProcessName = $"Game {index}",
                Success = true,
                AverageFps = 60 + index,
                FrameCount = 100
            })));

        var records = await service.GetRecentFpsAsync(20);

        Assert.Equal(10, records.Count);
        Assert.True(database.Factory.CreationCount >= 11);
    }

    [Fact]
    public async Task LegacyDatabaseIsMigratedAndBackfilledOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"patchguard-legacy-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = $"Data Source={path};Pooling=False";
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE "ScanRecords" (
                        "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        "Scenario" TEXT NOT NULL,
                        "ScannedAt" TEXT NOT NULL,
                        "FindingsJson" TEXT NOT NULL
                    );
                    INSERT INTO "ScanRecords" ("Scenario", "ScannedAt", "FindingsJson")
                    VALUES ($scenario, $scannedAt, $findings);
                    """;
                command.Parameters.AddWithValue("$scenario", ScanScenario.QuickHealthCheck.ToString());
                command.Parameters.AddWithValue("$scannedAt", DateTime.UtcNow);
                command.Parameters.AddWithValue(
                    "$findings",
                    JsonSerializer.Serialize(new[] { CreateFinding("Legacy") }));
                await command.ExecuteNonQueryAsync();
            }

            var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
                .UseSqlite(connectionString)
                .Options;
            var factory = new CountingDbContextFactory(options);
            var policy = new MutableHealthScorePolicy("migration-v1", 73);
            var initializer = new DatabaseSchemaInitializer(factory, policy);

            await initializer.InitializeAsync();
            policy.Score = 10;
            policy.Version = "migration-v2";
            await initializer.InitializeAsync();

            await using var context = await factory.CreateDbContextAsync();
            var record = Assert.Single(await context.ScanRecords.ToListAsync());
            Assert.Equal(73, record.HealthScore);
            Assert.Equal("migration-v1", record.ScorePolicyVersion);
            Assert.True(await context.FpsCaptures.AnyAsync() == false);
            Assert.True(await context.OptimizationRuns.AnyAsync() == false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Finding CreateFinding(string title) =>
        new()
        {
            ModuleName = "Test",
            Title = title,
            Details = "Measured evidence",
            Severity = FindingSeverity.Warning,
            Risk = FindingRisk.Medium
        };

    private sealed class MutableHealthScorePolicy(string version, int score) : IHealthScorePolicy
    {
        public string Version { get; set; } = version;
        public int Score { get; set; } = score;
        public int Calculate(IReadOnlyList<Finding> findings) => Score;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string _path;

        private TestDatabase(string path, CountingDbContextFactory factory)
        {
            _path = path;
            Factory = factory;
        }

        public CountingDbContextFactory Factory { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"patchguard-test-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<PatchGuardDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .Options;
            var factory = new CountingDbContextFactory(options);
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

    public sealed class CountingDbContextFactory(
        DbContextOptions<PatchGuardDbContext> options) : IDbContextFactory<PatchGuardDbContext>
    {
        private int _creationCount;
        public int CreationCount => _creationCount;

        public PatchGuardDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _creationCount);
            return new PatchGuardDbContext(options);
        }

        public Task<PatchGuardDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
