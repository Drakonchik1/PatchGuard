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
    public DbSet<CouncilEvaluationRecord> CouncilEvaluations => Set<CouncilEvaluationRecord>();
    public DbSet<SensorSnapshotRecord> SensorSnapshots => Set<SensorSnapshotRecord>();
    public DbSet<GuidedFixRunRecord> GuidedFixRuns => Set<GuidedFixRunRecord>();

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

        modelBuilder.Entity<CouncilEvaluationRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Scenario).HasMaxLength(64);
            entity.Property(e => e.Source).HasMaxLength(32);
            entity.HasIndex(e => e.EvaluatedAt);
        });

        modelBuilder.Entity<SensorSnapshotRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CapturedAt);
        });

        modelBuilder.Entity<GuidedFixRunRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Source).HasMaxLength(128);
            entity.Property(e => e.PlanTitle).HasMaxLength(256);
            entity.Property(e => e.Outcome).HasMaxLength(64);
            entity.Property(e => e.LinkedScanScenario).HasMaxLength(64);
            entity.Property(e => e.Summary).HasMaxLength(512);
            entity.HasIndex(e => e.RanAt);
        });
    }

}
