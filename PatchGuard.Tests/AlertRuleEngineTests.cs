using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Alerts;

namespace PatchGuard.Tests;

public sealed class AlertRuleEngineTests
{
    private readonly AlertRuleEngine _engine = new();

    [Fact]
    public void SyntheticCpuTempSpike_EmitsCriticalAlert()
    {
        var snapshot = new HardwareSnapshot
        {
            CpuTemperatureC = 98,
            CpuLoadPercent = 40,
            GpuTemperatureC = 60,
            GpuLoadPercent = 20
        };

        var alerts = _engine.Evaluate(snapshot);

        var critical = Assert.Single(alerts, a => a.Metric == "CpuTemperatureC");
        Assert.Equal(AlertSeverity.Critical, critical.Severity);
        Assert.Equal(AlertRuleEngine.CpuTempCriticalC, critical.Threshold);
        Assert.Contains("critical", critical.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(critical.RecommendedAction));
    }

    [Fact]
    public void SyntheticGpuTempWarning_EmitsWarningAlert()
    {
        var snapshot = new SensorSnapshotRecord
        {
            CapturedAt = DateTime.UtcNow,
            GpuTemperatureC = 88
        };

        var alerts = _engine.Evaluate(snapshot);

        var warning = Assert.Single(alerts);
        Assert.Equal("GpuTemperatureC", warning.Metric);
        Assert.Equal(AlertSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void HighCpuLoad_EmitsWarning()
    {
        var alerts = _engine.Evaluate(new HardwareSnapshot { CpuLoadPercent = 96 });

        var load = Assert.Single(alerts);
        Assert.Equal("CpuLoadPercent", load.Metric);
        Assert.Equal(AlertSeverity.Warning, load.Severity);
    }

    [Fact]
    public void HealthySnapshot_EmitsNoAlerts()
    {
        var alerts = _engine.Evaluate(new HardwareSnapshot
        {
            CpuTemperatureC = 55,
            CpuLoadPercent = 20,
            GpuTemperatureC = 50,
            GpuLoadPercent = 15
        });

        Assert.Empty(alerts);
    }

    [Fact]
    public void CriticalTempTakesPrecedenceOverWarningThreshold()
    {
        var alerts = _engine.Evaluate(new HardwareSnapshot { CpuTemperatureC = 99 });

        Assert.DoesNotContain(alerts, a => a.Severity == AlertSeverity.Warning && a.Metric == "CpuTemperatureC");
        Assert.Contains(alerts, a => a.Severity == AlertSeverity.Critical && a.Metric == "CpuTemperatureC");
    }
}
