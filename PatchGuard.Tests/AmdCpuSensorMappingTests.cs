using LibreHardwareMonitor.Hardware;
using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Tests;

public sealed class AmdCpuSensorMappingTests
{
    [Fact]
    public void ZeroTctl_IsIgnoredForCpuSummary()
    {
        var snapshot = new HardwareSnapshot();
        LibreHardwareMonitorService.ApplyCpu(
            snapshot, SensorType.Temperature, "Core (Tctl/Tdie)", 0);

        Assert.Null(snapshot.CpuTemperatureC);
    }

    [Fact]
    public void MotherboardCpuTemp_UsedWhenPackageTempMissing()
    {
        var snapshot = new HardwareSnapshot();
        LibreHardwareMonitorService.ApplyCpu(
            snapshot, SensorType.Temperature, "Core (Tctl/Tdie)", 0);
        LibreHardwareMonitorService.ApplyMotherboardCpuTemp(
            snapshot, SensorType.Temperature, "CPU", 58);

        Assert.Equal(58, snapshot.CpuTemperatureC);
    }

    [Fact]
    public void ZeroSmuPower_IsNotDisplayable()
    {
        Assert.False(LibreHardwareMonitorService.IsDisplayableSensor(SensorKind.Power, 0));
        Assert.False(LibreHardwareMonitorService.IsDisplayableSensor(SensorKind.Temperature, 0));
        Assert.True(LibreHardwareMonitorService.IsDisplayableSensor(SensorKind.Temperature, 62));
    }

    [Fact]
    public void GpuVrSoC_UsedWhenCoreTempMissing()
    {
        var snapshot = new HardwareSnapshot();
        LibreHardwareMonitorService.ApplyGpu(
            snapshot, SensorType.Temperature, "GPU VR SoC", 62);

        Assert.Equal(62, snapshot.GpuTemperatureC);
    }
}
