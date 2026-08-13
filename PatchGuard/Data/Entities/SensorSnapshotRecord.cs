namespace PatchGuard.Data.Entities;

/// <summary>
/// Numeric-only hardware sample for history, alerts, and future ML.
/// No device names or other PII.
/// </summary>
public sealed class SensorSnapshotRecord
{
    public int Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public double? CpuTemperatureC { get; set; }
    public double? CpuLoadPercent { get; set; }
    public double? GpuTemperatureC { get; set; }
    public double? GpuLoadPercent { get; set; }
    public double? RamLoadPercent { get; set; }
    public double? RamUsedGb { get; set; }
}
