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
            entity.Property(e => e.ScorePolicyVersion).HasMaxLength(64);
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

}
