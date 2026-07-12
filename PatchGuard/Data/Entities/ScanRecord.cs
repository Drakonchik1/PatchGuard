namespace PatchGuard.Data.Entities;

public sealed class ScanRecord
{
    public int Id { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; }
    public string FindingsJson { get; set; } = "[]";
    public int? HealthScore { get; set; }
    public string? ScorePolicyVersion { get; set; }
}
