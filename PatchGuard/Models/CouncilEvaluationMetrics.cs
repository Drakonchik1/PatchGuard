namespace PatchGuard.Models;

public sealed record CouncilEvaluationMetrics
{
    public double ActionabilityScore { get; init; }
    public double ConsistencyScore { get; init; }
}
