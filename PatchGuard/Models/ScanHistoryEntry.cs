namespace PatchGuard.Models;

public sealed record ScanHistoryEntry
{
    public int Id { get; init; }
    public DateTime ScannedAt { get; init; }
    public string ScenarioTitle { get; init; } = string.Empty;
    public int FindingCount { get; init; }
    public int WarningCount { get; init; }
    public int HealthScore { get; init; }
    public string TrendLabel { get; init; } = "—";
}
