using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Health;

namespace PatchGuard.Services.History;

public sealed class ScanHistoryService : IScanHistoryService
{
    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;
    private readonly IHealthScorePolicy _healthScorePolicy;

    public ScanHistoryService(
        IDbContextFactory<PatchGuardDbContext> dbContextFactory,
        IHealthScorePolicy healthScorePolicy)
    {
        _dbContextFactory = dbContextFactory;
        _healthScorePolicy = healthScorePolicy;
    }

    public async Task SaveScanAsync(
        ScanScenario scenario,
        IReadOnlyList<Finding> findings,
        CancellationToken cancellationToken = default)
    {
        var record = new ScanRecord
        {
            Scenario = scenario.ToString(),
            ScannedAt = DateTime.UtcNow,
            FindingsJson = JsonSerializer.Serialize(findings),
            HealthScore = _healthScorePolicy.Calculate(findings),
            ScorePolicyVersion = _healthScorePolicy.Version
        };

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ScanRecords.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
        int take = 6,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.ScanRecords
            .AsNoTracking()
            .OrderByDescending(r => r.ScannedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var entries = records.Select(record => MapEntry(record, _healthScorePolicy)).ToList();

        for (var i = 0; i < entries.Count - 1; i++)
        {
            entries[i] = entries[i] with
            {
                TrendLabel = BuildTrend(entries[i].HealthScore, entries[i + 1].HealthScore)
            };
        }

        return entries;
    }

    private static ScanHistoryEntry MapEntry(ScanRecord record, IHealthScorePolicy healthScorePolicy)
    {
        var findings = JsonSerializer.Deserialize<List<Finding>>(record.FindingsJson) ?? [];
        var warnings = findings.Count(f => f.Severity >= FindingSeverity.Warning);
        var health = record.HealthScore ?? healthScorePolicy.Calculate(findings);

        Enum.TryParse<ScanScenario>(record.Scenario, out var scenario);

        return new ScanHistoryEntry
        {
            Id = record.Id,
            ScannedAt = record.ScannedAt.ToLocalTime(),
            ScenarioTitle = scenario != default ? scenario.GetTitle() : record.Scenario,
            FindingCount = findings.Count,
            WarningCount = warnings,
            HealthScore = health,
            TrendLabel = "—"
        };
    }

    private static string BuildTrend(int current, int previous)
    {
        var delta = current - previous;
        return delta switch
        {
            > 5 => $"▲ +{delta} vs prior",
            < -5 => $"▼ {delta} vs prior",
            _ => "≈ stable"
        };
    }

}
