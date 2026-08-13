using PatchGuard.Models;
using PatchGuard.Services.History;
using PatchGuard.Services.Ml;

namespace PatchGuard.Services.Diagnostics;

/// <summary>
/// Emits findings from inference-only anomaly detection over sensor history.
/// </summary>
public sealed class AnomalyDiagnosticModule : IDiagnosticModule
{
    private readonly ISensorHistoryService _sensorHistory;
    private readonly IAnomalyDetector _anomalyDetector;

    public AnomalyDiagnosticModule(
        ISensorHistoryService sensorHistory,
        IAnomalyDetector anomalyDetector)
    {
        _sensorHistory = sensorHistory;
        _anomalyDetector = anomalyDetector;
    }

    public string Name => "Anomaly detection";
    public string Description =>
        "Scores recent sensor history with a bundled Isolation Forest / z-score baseline (inference only).";
    public bool IsImplemented => true;

    public async Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var history = await _sensorHistory.GetRecentAsync(take: 200, cancellationToken);
        if (history.Count < ZScoreAnomalyDetector.DefaultMinSamples)
        {
            return
            [
                new Finding
                {
                    ModuleName = Name,
                    Title = "Not enough sensor history for anomaly detection",
                    Details =
                        $"Need at least {ZScoreAnomalyDetector.DefaultMinSamples} snapshots; open Live Monitor to collect samples.",
                    Severity = FindingSeverity.Info,
                    Evidence = $"Sensor history contains {history.Count} snapshot(s).",
                    ActionState = FindingActionState.None,
                    AdminRequirement = FindingAdminRequirement.NotRequired,
                    Risk = FindingRisk.NotApplicable,
                    VerificationStatus = FindingVerificationStatus.NotRequired
                }
            ];
        }

        var hits = _anomalyDetector.Detect(history);
        if (hits.Count == 0)
        {
            return
            [
                new Finding
                {
                    ModuleName = Name,
                    Title = "No sensor anomalies detected",
                    Details =
                        $"Detector '{_anomalyDetector.Name}' found no outliers in the last {history.Count} snapshots.",
                    Severity = FindingSeverity.Info,
                    Evidence = $"Scored {history.Count} snapshots with {_anomalyDetector.Name}.",
                    ActionState = FindingActionState.None,
                    AdminRequirement = FindingAdminRequirement.NotRequired,
                    Risk = FindingRisk.NotApplicable,
                    VerificationStatus = FindingVerificationStatus.NotRequired
                }
            ];
        }

        return hits.Select(hit => new Finding
        {
            ModuleName = Name,
            Title = $"{hit.SensorName} anomaly ({hit.ConfidencePercent:F0}% confidence)",
            Details = hit.Explanation,
            Severity = hit.Severity,
            Evidence = hit.Explanation,
            Recommendation = hit.Severity >= FindingSeverity.Warning
                ? "Check cooling, background load, and recent games/apps. Re-open Live Monitor after changes to refresh history."
                : null,
            ActionState = hit.Severity >= FindingSeverity.Warning
                ? FindingActionState.Recommended
                : FindingActionState.None,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = hit.Severity >= FindingSeverity.Warning ? FindingRisk.Medium : FindingRisk.Low,
            VerificationStatus = hit.Severity >= FindingSeverity.Warning
                ? FindingVerificationStatus.NotVerified
                : FindingVerificationStatus.NotRequired
        }).ToList();
    }
}
