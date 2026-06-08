using Microsoft.EntityFrameworkCore;
using PatchGuard.Data.Entities;

namespace PatchGuard.Data;

public sealed class PatchGuardDbContext : DbContext
{
    public PatchGuardDbContext(DbContextOptions<PatchGuardDbContext> options)
        : base(options)
    {
    }

    public DbSet<ScanRecord> ScanRecords => Set<ScanRecord>();
    public DbSet<BenchmarkRecord> BenchmarkRecords => Set<BenchmarkRecord>();
    public DbSet<GameFpsEntry> GameFpsEntries => Set<GameFpsEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Scenario).HasMaxLength(64);
            entity.HasIndex(e => e.ScannedAt);
        });

        modelBuilder.Entity<BenchmarkRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RecordedAt);
        });

        modelBuilder.Entity<GameFpsEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GameName).HasMaxLength(128);
            entity.HasIndex(e => e.RecordedAt);
        });
    }

    public void EnsureExtendedSchema()
    {
        Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS BenchmarkRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordedAt TEXT NOT NULL,
                SyntheticFps REAL NOT NULL,
                GpuName TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS GameFpsEntries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameName TEXT NOT NULL,
                Fps INTEGER NOT NULL,
                RecordedAt TEXT NOT NULL,
                Note TEXT NULL
            );
            """);
    }
}
