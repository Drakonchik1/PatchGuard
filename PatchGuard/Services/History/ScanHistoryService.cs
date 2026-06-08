using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.History;

public sealed class ScanHistoryService : IScanHistoryService
{
    private readonly PatchGuardDbContext _dbContext;

    public ScanHistoryService(PatchGuardDbContext dbContext)
    {
        _dbContext = dbContext;
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
            FindingsJson = JsonSerializer.Serialize(findings)
        };

        _dbContext.ScanRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetRecentScansAsync(
        int take = 6,
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.ScanRecords
            .OrderByDescending(r => r.ScannedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var entries = records.Select(MapEntry).ToList();

        for (var i = 0; i < entries.Count - 1; i++)
        {
            entries[i] = entries[i] with
            {
                TrendLabel = BuildTrend(entries[i].HealthScore, entries[i + 1].HealthScore)
            };
        }

        return entries;
    }

    private static ScanHistoryEntry MapEntry(ScanRecord record)
    {
        var findings = JsonSerializer.Deserialize<List<Finding>>(record.FindingsJson) ?? [];
        var warnings = findings.Count(f => f.Severity >= FindingSeverity.Warning);
        var health = ComputeHealthScore(findings);

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

    private static int ComputeHealthScore(IReadOnlyList<Finding> findings) =>
        Math.Clamp(100 - findings.Sum(f => f.Severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.Warning => 12,
            _ => 0
        }), 15, 100);
}
