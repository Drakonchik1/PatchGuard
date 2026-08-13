namespace PatchGuard.Services.Hardware;

/// <summary>
/// Secondary CPU temperature source used when LibreHardwareMonitor's AMD SMU
/// path returns stub zeros (common on Hawk Point / Strix Point Ryzen laptops).
/// </summary>
public interface IOsThermalTemperatureSource
{
    double? TryReadCpuTemperatureC();
}
