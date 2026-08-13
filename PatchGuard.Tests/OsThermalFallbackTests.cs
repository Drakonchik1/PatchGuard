using PatchGuard.Services.Hardware;

namespace PatchGuard.Tests;

public sealed class OsThermalFallbackTests
{
    [Fact]
    public void ZeroTctl_DoesNotBlockOsThermalFallbackValue()
    {
        var snapshot = new Models.HardwareSnapshot();
        LibreHardwareMonitorService.ApplyCpu(
            snapshot,
            LibreHardwareMonitor.Hardware.SensorType.Temperature,
            "Core (Tctl/Tdie)",
            0);

        Assert.Null(snapshot.CpuTemperatureC);

        var osTemp = new FakeOsThermal(79.2).TryReadCpuTemperatureC();
        Assert.Equal(79.2, osTemp);
        Assert.True(LibreHardwareMonitorService.IsPlausibleTemperature(osTemp!.Value));
    }

    [Fact]
    public void WindowsThermalZone_ReadsPlausibleTemperatureWhenAvailable()
    {
        using var source = new WindowsThermalZoneTemperatureSource();
        var temp = source.TryReadCpuTemperatureC();
        if (temp is null)
        {
            return;
        }

        Assert.True(
            LibreHardwareMonitorService.IsPlausibleTemperature(temp.Value),
            $"Unexpected thermal zone reading: {temp}");
    }

    private sealed class FakeOsThermal(double? value) : IOsThermalTemperatureSource
    {
        public double? TryReadCpuTemperatureC() => value;
    }
}
