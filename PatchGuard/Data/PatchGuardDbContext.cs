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
    public DbSet<FpsCaptureRecord> FpsCaptures => Set<FpsCaptureRecord>();
    public DbSet<OptimizationRunRecord> OptimizationRuns => Set<OptimizationRunRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScanRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Scenario).HasMaxLength(64);
            entity.HasIndex(e => e.ScannedAt);
        });

        modelBuilder.Entity<FpsCaptureRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProcessName).HasMaxLength(256);
            entity.HasIndex(e => e.CapturedAt);
        });

        modelBuilder.Entity<OptimizationRunRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Summary).HasMaxLength(512);
            entity.HasIndex(e => e.RanAt);
        });
    }

    /// <summary>
    /// EnsureCreated() only builds the schema for brand-new databases. For users
    /// upgrading from an older PatchGuard whose database already exists, this adds
    /// the new tables in place without touching existing data.
    /// </summary>
    public void EnsureUpgradeSchema()
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "FpsCaptures" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FpsCaptures" PRIMARY KEY AUTOINCREMENT,
                "ProcessName" TEXT NOT NULL DEFAULT '',
                "AverageFps" REAL NOT NULL DEFAULT 0,
                "OnePercentLowFps" REAL NOT NULL DEFAULT 0,
                "PointOnePercentLowFps" REAL NOT NULL DEFAULT 0,
                "FrameCount" INTEGER NOT NULL DEFAULT 0,
                "CapturedAt" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_FpsCaptures_CapturedAt" ON "FpsCaptures" ("CapturedAt");

            CREATE TABLE IF NOT EXISTS "OptimizationRuns" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OptimizationRuns" PRIMARY KEY AUTOINCREMENT,
                "RanAt" TEXT NOT NULL,
                "BytesFreed" INTEGER NOT NULL DEFAULT 0,
                "StepsSucceeded" INTEGER NOT NULL DEFAULT 0,
                "Summary" TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS "IX_OptimizationRuns_RanAt" ON "OptimizationRuns" ("RanAt");
            """;

        Database.ExecuteSqlRaw(sql);
    }
}
