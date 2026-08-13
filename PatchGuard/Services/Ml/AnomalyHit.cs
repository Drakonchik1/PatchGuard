using PatchGuard.Models;

namespace PatchGuard.Services.Ml;

/// <summary>Single sensor anomaly with confidence and a human-readable explanation.</summary>
public sealed class AnomalyHit
{
    public required string SensorName { get; init; }
    public required string MetricKey { get; init; }
    public double Value { get; init; }
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double ZScore { get; init; }
    /// <summary>0–100 confidence that this reading is anomalous.</summary>
    public double ConfidencePercent { get; init; }
    public required string Explanation { get; init; }
    public FindingSeverity Severity { get; init; }
    public required string DetectorName { get; init; }
}
