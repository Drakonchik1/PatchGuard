using PatchGuard.Data.Entities;

namespace PatchGuard.Services.Ml;

/// <summary>Extracts named univariate series from numeric sensor snapshots.</summary>
public static class SensorMetricSeries
{
    public const string CpuTemperatureC = "CpuTemperatureC";
    public const string CpuLoadPercent = "CpuLoadPercent";
    public const string GpuTemperatureC = "GpuTemperatureC";
    public const string GpuLoadPercent = "GpuLoadPercent";
    public const string RamLoadPercent = "RamLoadPercent";

    public static readonly IReadOnlyList<(string Key, string DisplayName, Func<SensorSnapshotRecord, double?> Selector)> Metrics =
    [
        (CpuTemperatureC, "CPU temperature", r => r.CpuTemperatureC),
        (CpuLoadPercent, "CPU load", r => r.CpuLoadPercent),
        (GpuTemperatureC, "GPU temperature", r => r.GpuTemperatureC),
        (GpuLoadPercent, "GPU load", r => r.GpuLoadPercent),
        (RamLoadPercent, "RAM load", r => r.RamLoadPercent)
    ];

    public static IReadOnlyList<double> Extract(
        IReadOnlyList<SensorSnapshotRecord> history,
        Func<SensorSnapshotRecord, double?> selector)
    {
        // Chronological order (oldest → newest) for baseline vs latest scoring.
        var values = new List<double>(history.Count);
        foreach (var record in history.OrderBy(r => r.CapturedAt))
        {
            if (selector(record) is { } value && !double.IsNaN(value) && !double.IsInfinity(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    public static string FormatValue(string metricKey, double value) =>
        metricKey is CpuTemperatureC or GpuTemperatureC
            ? $"{value:F0}°C"
            : $"{value:F0}%";
}
