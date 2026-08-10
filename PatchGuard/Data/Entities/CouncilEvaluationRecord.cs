namespace PatchGuard.Data.Entities;

/// <summary>
/// Aggregate metrics for one AI council session. Counts and scores only — no PII or raw guide text.
/// </summary>
public sealed class CouncilEvaluationRecord
{
    public int Id { get; set; }
    public DateTime EvaluatedAt { get; set; }
    public string Scenario { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public int FixStepCount { get; set; }
    public int CouncilMessageCount { get; set; }
    public double? ActionabilityScore { get; set; }
    public double? ConsistencyScore { get; set; }
}
