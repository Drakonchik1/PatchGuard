using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PatchGuard.Models;
using PatchGuard.Services.Health;

namespace PatchGuard.Data;

public sealed class DatabaseSchemaInitializer
{
    private const string SnapshotMigration = "20260712_HealthScoreSnapshots";
    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;
    private readonly IHealthScorePolicy _healthScorePolicy;

    public DatabaseSchemaInitializer(
        IDbContextFactory<PatchGuardDbContext> dbContextFactory,
        IHealthScorePolicy healthScorePolicy)
    {
        _dbContextFactory = dbContextFactory;
        _healthScorePolicy = healthScorePolicy;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "ScanRecords" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ScanRecords" PRIMARY KEY AUTOINCREMENT,
                "Scenario" TEXT NOT NULL,
                "ScannedAt" TEXT NOT NULL,
                "FindingsJson" TEXT NOT NULL,
                "HealthScore" INTEGER NULL,
                "ScorePolicyVersion" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS "IX_ScanRecords_ScannedAt" ON "ScanRecords" ("ScannedAt");

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

            CREATE TABLE IF NOT EXISTS "SchemaMigrations" (
                "MigrationId" TEXT NOT NULL CONSTRAINT "PK_SchemaMigrations" PRIMARY KEY,
                "AppliedAt" TEXT NOT NULL
            );
            """,
            cancellationToken);

        if (!await ColumnExistsAsync(dbContext, "ScanRecords", "HealthScore", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ScanRecords" ADD COLUMN "HealthScore" INTEGER NULL;""",
                cancellationToken);
        }

        if (!await ColumnExistsAsync(dbContext, "ScanRecords", "ScorePolicyVersion", cancellationToken))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "ScanRecords" ADD COLUMN "ScorePolicyVersion" TEXT NULL;""",
                cancellationToken);
        }

        var legacyRecords = await dbContext.ScanRecords
            .Where(record => record.HealthScore == null || record.ScorePolicyVersion == null)
            .ToListAsync(cancellationToken);
        foreach (var record in legacyRecords)
        {
            var findings =
                JsonSerializer.Deserialize<List<Finding>>(record.FindingsJson) ?? [];
            record.HealthScore = _healthScorePolicy.Calculate(findings);
            record.ScorePolicyVersion = _healthScorePolicy.Version;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT OR IGNORE INTO "SchemaMigrations" ("MigrationId", "AppliedAt")
             VALUES ({SnapshotMigration}, {DateTime.UtcNow});
             """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        PatchGuardDbContext dbContext,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess,
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
