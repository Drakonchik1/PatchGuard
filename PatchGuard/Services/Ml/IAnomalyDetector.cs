using PatchGuard.Data.Entities;

namespace PatchGuard.Services.Ml;

/// <summary>
/// Inference-only anomaly detection over sensor history.
/// Training happens offline; the product never exposes a train UI.
/// </summary>
public interface IAnomalyDetector
{
    string Name { get; }

    /// <summary>True when a bundled model (or always-on baseline) can score.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Scores the newest samples against history. Empty when history is too short
    /// or no anomaly is present.
    /// </summary>
    IReadOnlyList<AnomalyHit> Detect(IReadOnlyList<SensorSnapshotRecord> history);
}
