using LibreHardwareMonitor.Hardware;
using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Tests;

public sealed class AmdGpuSensorMappingTests
{
    [Fact]
    public void AmdAdlCoreSensors_MapToGpuSummary()
    {
        var snapshot = new HardwareSnapshot();

        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.Temperature, "GPU Core", 72);
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.Load, "GPU Core", 41);
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.SmallData, "GPU Memory Used", 2048);
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.SmallData, "GPU Memory Total", 8192);
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.Power, "GPU Package", 95);

        Assert.Equal(72, snapshot.GpuTemperatureC);
        Assert.Equal(41, snapshot.GpuLoadPercent);
        Assert.Equal(2048, snapshot.GpuMemoryUsedMb);
        Assert.Equal(8192, snapshot.GpuMemoryTotalMb);
        Assert.Equal(95, snapshot.GpuPowerWatts);
    }

    [Fact]
    public void AmdHotSpot_UsedWhenCoreMissing()
    {
        var snapshot = new HardwareSnapshot();
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.Temperature, "GPU Hot Spot", 91);

        Assert.Equal(91, snapshot.GpuTemperatureC);
    }

    [Fact]
    public void AmdD3dLoad_UsedWhenCoreLoadMissing()
    {
        var snapshot = new HardwareSnapshot();
        LibreHardwareMonitorService.ApplyGpu(snapshot, SensorType.Load, "D3D 3D", 55);

        Assert.Equal(55, snapshot.GpuLoadPercent);
    }

    [Fact]
    public void DiscreteAmdPreferredOverIntel()
    {
        Assert.True(
            LibreHardwareMonitorService.GpuPreference(HardwareType.GpuAmd)
            > LibreHardwareMonitorService.GpuPreference(HardwareType.GpuIntel));
    }
}
