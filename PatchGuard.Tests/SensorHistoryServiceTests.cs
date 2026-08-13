using System.IO;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Models;
using PatchGuard.Services.History;

namespace PatchGuard.Tests;

public sealed class SensorHistoryServiceTests
{
    [Fact]
    public async Task SaveSnapshot_PersistsNumericFieldsOnly()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new SensorHistoryService(database.Factory);

        await service.SaveSnapshotAsync(new HardwareSnapshot
        {
            CpuName = "should-not-persist",
            GpuName = "should-not-persist",
            CpuTemperatureC = 71,
            CpuLoadPercent = 33,
            GpuTemperatureC = 64,
            GpuLoadPercent = 12,
            RamLoadPercent = 48,
            RamUsedGb = 9.5
        });

        var latest = await service.GetLatestAsync();
        Assert.NotNull(latest);
        Assert.Equal(71, latest.CpuTemperatureC);
        Assert.Equal(33, latest.CpuLoadPercent);
        Assert.Equal(64, latest.GpuTemperatureC);
        Assert.Equal(12, latest.GpuLoadPercent);
        Assert.Equal(48, latest.RamLoadPercent);
        Assert.Equal(9.5, latest.RamUsedGb);
    }

    [Fact]
    public async Task SaveSnapshot_PrunesOlderThanRetention()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new SensorHistoryService(database.Factory, TimeSpan.FromHours(1));

        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.SensorSnapshots.Add(new Data.Entities.SensorSnapshotRecord
            {
                CapturedAt = DateTime.UtcNow.AddHours(-3),
                CpuLoadPercent = 10
            });
            context.SensorSnapshots.Add(new Data.Entities.SensorSnapshotRecord
            {
                CapturedAt = DateTime.UtcNow.AddMinutes(-10),
                CpuLoadPercent = 20
            });
            await context.SaveChangesAsync();
        }

        await service.SaveSnapshotAsync(new HardwareSnapshot { CpuLoadPercent = 30 });

        var recent = await service.GetRecentAsync(20);
        Assert.Equal(2, recent.Count);
        Assert.All(recent, r => Assert.True(r.CpuLoadPercent is 20 or 30));
    }

    [Fact]
    public async Task ConcurrentSaves_UseIndependentContexts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var service = new SensorHistoryService(database.Factory);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            service.SaveSnapshotAsync(new HardwareSnapshot { CpuLoadPercent = i })));

        var recent = await service.GetRecentAsync(20);
        Assert.Equal(12, recent.Count);
        Assert.True(database.Factory.CreationCount >= 13);
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
            var path = Path.Combine(Path.GetTempPath(), $"patchguard-sensor-{Guid.NewGuid():N}.db");
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

    private sealed class CountingDbContextFactory(
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
