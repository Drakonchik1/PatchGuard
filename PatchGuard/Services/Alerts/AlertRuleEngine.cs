using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Alerts;

public sealed class AlertRuleEngine : IAlertRuleEngine
{
    public const double CpuTempWarningC = 85;
    public const double CpuTempCriticalC = 95;
    public const double GpuTempWarningC = 85;
    public const double GpuTempCriticalC = 95;
    public const double CpuLoadWarningPercent = 90;
    public const double GpuLoadWarningPercent = 95;

    public IReadOnlyList<Alert> Evaluate(HardwareSnapshot snapshot) =>
        Evaluate(
            snapshot.CapturedAt.ToUniversalTime(),
            snapshot.CpuTemperatureC,
            snapshot.CpuLoadPercent,
            snapshot.GpuTemperatureC,
            snapshot.GpuLoadPercent);

    public IReadOnlyList<Alert> Evaluate(SensorSnapshotRecord snapshot) =>
        Evaluate(
            snapshot.CapturedAt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(snapshot.CapturedAt, DateTimeKind.Utc)
                : snapshot.CapturedAt.ToUniversalTime(),
            snapshot.CpuTemperatureC,
            snapshot.CpuLoadPercent,
            snapshot.GpuTemperatureC,
            snapshot.GpuLoadPercent);

    private static IReadOnlyList<Alert> Evaluate(
        DateTime timestampUtc,
        double? cpuTempC,
        double? cpuLoadPercent,
        double? gpuTempC,
        double? gpuLoadPercent)
    {
        var alerts = new List<Alert>(4);
        var timestamp = timestampUtc.Kind == DateTimeKind.Utc
            ? timestampUtc.ToLocalTime()
            : timestampUtc;

        AddTemperatureAlert(
            alerts,
            timestamp,
            metric: "CpuTemperatureC",
            label: "CPU temperature",
            value: cpuTempC,
            warningThreshold: CpuTempWarningC,
            criticalThreshold: CpuTempCriticalC,
            coolDownHint: "Improve CPU cooling, check thermal paste, and reduce sustained load.");

        AddTemperatureAlert(
            alerts,
            timestamp,
            metric: "GpuTemperatureC",
            label: "GPU temperature",
            value: gpuTempC,
            warningThreshold: GpuTempWarningC,
            criticalThreshold: GpuTempCriticalC,
            coolDownHint: "Raise GPU fan curve, clean dust, or lower graphics settings.");

        if (cpuLoadPercent is { } cpuLoad && cpuLoad > CpuLoadWarningPercent)
        {
            alerts.Add(new Alert
            {
                Id = "cpu-load-high",
                Severity = AlertSeverity.Warning,
                Timestamp = timestamp,
                Metric = "CpuLoadPercent",
                Value = cpuLoad,
                Threshold = CpuLoadWarningPercent,
                Message = $"CPU load is {cpuLoad:F0}% (threshold {CpuLoadWarningPercent:F0}%).",
                RecommendedAction = "Identify high-CPU processes and close unused apps or background tasks."
            });
        }

        if (gpuLoadPercent is { } gpuLoad && gpuLoad > GpuLoadWarningPercent)
        {
            alerts.Add(new Alert
            {
                Id = "gpu-load-high",
                Severity = AlertSeverity.Warning,
                Timestamp = timestamp,
                Metric = "GpuLoadPercent",
                Value = gpuLoad,
                Threshold = GpuLoadWarningPercent,
                Message = $"GPU load is {gpuLoad:F0}% (threshold {GpuLoadWarningPercent:F0}%).",
                RecommendedAction = "Lower in-game settings or close other GPU-heavy applications."
            });
        }

        return alerts;
    }

    private static void AddTemperatureAlert(
        List<Alert> alerts,
        DateTime timestamp,
        string metric,
        string label,
        double? value,
        double warningThreshold,
        double criticalThreshold,
        string coolDownHint)
    {
        if (value is not { } temp)
        {
            return;
        }

        if (temp > criticalThreshold)
        {
            alerts.Add(new Alert
            {
                Id = $"{metric.ToLowerInvariant()}-critical",
                Severity = AlertSeverity.Critical,
                Timestamp = timestamp,
                Metric = metric,
                Value = temp,
                Threshold = criticalThreshold,
                Message = $"{label} is {temp:F0} °C (critical above {criticalThreshold:F0} °C).",
                RecommendedAction = coolDownHint
            });
            return;
        }

        if (temp > warningThreshold)
        {
            alerts.Add(new Alert
            {
                Id = $"{metric.ToLowerInvariant()}-warning",
                Severity = AlertSeverity.Warning,
                Timestamp = timestamp,
                Metric = metric,
                Value = temp,
                Threshold = warningThreshold,
                Message = $"{label} is {temp:F0} °C (warning above {warningThreshold:F0} °C).",
                RecommendedAction = coolDownHint
            });
        }
    }
}
