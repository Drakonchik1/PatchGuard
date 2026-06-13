namespace PatchGuard.Models;

/// <summary>
/// A point-in-time reading of system hardware. Any value that could not be read
/// (for example a temperature sensor that needs admin rights) is left null so
/// the UI can show "n/a" rather than a misleading zero.
/// </summary>
public sealed class HardwareSnapshot
{
    public DateTime CapturedAt { get; init; } = DateTime.Now;

    public string CpuName { get; set; } = "CPU";
    public double? CpuTemperatureC { get; set; }
    public double? CpuLoadPercent { get; set; }
    public double? CpuPowerWatts { get; set; }
    public double? CpuClockMhz { get; set; }

    public string GpuName { get; set; } = "GPU";
    public double? GpuTemperatureC { get; set; }
    public double? GpuLoadPercent { get; set; }
    public double? GpuMemoryUsedMb { get; set; }
    public double? GpuMemoryTotalMb { get; set; }
    public double? GpuPowerWatts { get; set; }

    public double? RamUsedGb { get; set; }
    public double? RamTotalGb { get; set; }
    public double? RamLoadPercent { get; set; }

    public List<SensorReading> Sensors { get; } = [];

    /// <summary>
    /// True when low-level sensors (especially temperatures) were unavailable,
    /// typically because PatchGuard is not running as administrator.
    /// </summary>
    public bool SensorsLimited { get; set; }

    /// <summary>True when the hardware library could not be initialised at all.</summary>
    public bool MonitorUnavailable { get; set; }

    public string? StatusMessage { get; set; }
}
