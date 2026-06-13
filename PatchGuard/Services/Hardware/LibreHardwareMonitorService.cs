using LibreHardwareMonitor.Hardware;
using PatchGuard.Models;
using PatchGuard.Services.Native;
using PatchGuard.Services.Platform;

namespace PatchGuard.Services.Hardware;

/// <summary>
/// Wraps LibreHardwareMonitorLib. The underlying <see cref="Computer"/> is not
/// thread-safe, so every read is serialised behind a lock. Full sensor coverage
/// (CPU/GPU temperatures, fan/clock) generally requires administrator rights;
/// without them we still report load and RAM and flag the snapshot as limited.
/// </summary>
public sealed class LibreHardwareMonitorService : IHardwareMonitorService
{
    private readonly IAdminElevationService _elevation;
    private readonly object _gate = new();
    private readonly UpdateVisitor _visitor = new();

    private Computer? _computer;
    private bool _initFailed;
    private bool _disposed;

    public LibreHardwareMonitorService(IAdminElevationService elevation)
    {
        _elevation = elevation;
    }

    public HardwareSnapshot Capture()
    {
        lock (_gate)
        {
            var snapshot = new HardwareSnapshot();

            if (_disposed)
            {
                snapshot.MonitorUnavailable = true;
                snapshot.StatusMessage = "Hardware monitor has been shut down.";
                return snapshot;
            }

            if (!TryEnsureComputer(snapshot))
            {
                FillRamFromOs(snapshot);
                return snapshot;
            }

            try
            {
                _computer!.Accept(_visitor);
                foreach (var hardware in _computer.Hardware)
                {
                    ReadHardware(hardware, snapshot);
                }
            }
            catch (Exception ex)
            {
                snapshot.StatusMessage = $"Sensor read error: {ex.Message}";
            }

            if (snapshot.RamTotalGb is null or 0)
            {
                FillRamFromOs(snapshot);
            }

            snapshot.SensorsLimited = snapshot is { CpuTemperatureC: null, GpuTemperatureC: null }
                                      && !_elevation.IsElevated;

            return snapshot;
        }
    }

    private bool TryEnsureComputer(HardwareSnapshot snapshot)
    {
        if (_computer is not null)
        {
            return true;
        }

        if (_initFailed)
        {
            snapshot.MonitorUnavailable = true;
            snapshot.StatusMessage = "Hardware monitoring library could not be initialised on this system.";
            return false;
        }

        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };
            computer.Open();
            _computer = computer;
            return true;
        }
        catch (Exception ex)
        {
            _initFailed = true;
            snapshot.MonitorUnavailable = true;
            snapshot.StatusMessage = $"Hardware monitoring unavailable: {ex.Message}";
            return false;
        }
    }

    private static void ReadHardware(IHardware hardware, HardwareSnapshot snapshot)
    {
        var isCpu = hardware.HardwareType == HardwareType.Cpu;
        var isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var isMemory = hardware.HardwareType == HardwareType.Memory;

        if (isCpu)
        {
            snapshot.CpuName = hardware.Name;
        }
        else if (isGpu)
        {
            snapshot.GpuName = hardware.Name;
        }

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float raw || float.IsNaN(raw))
            {
                continue;
            }

            var value = raw;
            var kind = MapKind(sensor.SensorType);
            if (kind is not null)
            {
                snapshot.Sensors.Add(new SensorReading
                {
                    Hardware = hardware.Name,
                    Name = sensor.Name,
                    Kind = kind.Value,
                    Value = value,
                    Unit = UnitFor(kind.Value)
                });
            }

            if (isCpu)
            {
                ApplyCpu(snapshot, sensor, value);
            }
            else if (isGpu)
            {
                ApplyGpu(snapshot, sensor, value);
            }
            else if (isMemory)
            {
                ApplyMemory(snapshot, sensor, value);
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            ReadHardware(sub, snapshot);
        }
    }

    private static void ApplyCpu(HardwareSnapshot snapshot, ISensor sensor, double value)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Temperature when IsPreferredCpuTemp(sensor.Name):
                snapshot.CpuTemperatureC = value;
                break;
            case SensorType.Temperature:
                // Fall back to the hottest core if no package sensor is present.
                snapshot.CpuTemperatureC = snapshot.CpuTemperatureC is { } existing
                    ? Math.Max(existing, value)
                    : value;
                break;
            case SensorType.Load when sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase):
                snapshot.CpuLoadPercent = value;
                break;
            case SensorType.Power when sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                snapshot.CpuPowerWatts = value;
                break;
            case SensorType.Clock when snapshot.CpuClockMhz is null && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                snapshot.CpuClockMhz = value;
                break;
        }
    }

    private static void ApplyGpu(HardwareSnapshot snapshot, ISensor sensor, double value)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Temperature when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
            case SensorType.Temperature when snapshot.GpuTemperatureC is null:
                snapshot.GpuTemperatureC = value;
                break;
            case SensorType.Load when sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuLoadPercent = value;
                break;
            case SensorType.SmallData when sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuMemoryUsedMb = value;
                break;
            case SensorType.SmallData when sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuMemoryTotalMb = value;
                break;
            case SensorType.Power:
                snapshot.GpuPowerWatts = value;
                break;
        }
    }

    private static void ApplyMemory(HardwareSnapshot snapshot, ISensor sensor, double value)
    {
        switch (sensor.SensorType)
        {
            case SensorType.Load when sensor.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase):
                snapshot.RamLoadPercent = value;
                break;
            case SensorType.Data when sensor.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase):
                snapshot.RamUsedGb = value;
                break;
            case SensorType.Data when sensor.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase):
                // Used + Available = total physical (approx).
                if (snapshot.RamUsedGb is { } used)
                {
                    snapshot.RamTotalGb = used + value;
                }
                break;
        }
    }

    private static void FillRamFromOs(HardwareSnapshot snapshot)
    {
        try
        {
            var status = new NativeMethods.MEMORYSTATUSEX();
            if (NativeMethods.GlobalMemoryStatusEx(status))
            {
                const double bytesPerGb = 1024d * 1024 * 1024;
                var total = status.ullTotalPhys / bytesPerGb;
                var avail = status.ullAvailPhys / bytesPerGb;
                snapshot.RamTotalGb = total;
                snapshot.RamUsedGb = total - avail;
                snapshot.RamLoadPercent = status.dwMemoryLoad;
            }
        }
        catch
        {
            // Leave RAM values null; UI renders "n/a".
        }
    }

    private static bool IsPreferredCpuTemp(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("CPU", StringComparison.OrdinalIgnoreCase);

    private static SensorKind? MapKind(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Power => SensorKind.Power,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Data or SensorType.SmallData => SensorKind.Data,
        _ => null
    };

    private static string UnitFor(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => "°C",
        SensorKind.Load => "%",
        SensorKind.Clock => "MHz",
        SensorKind.Fan => "RPM",
        SensorKind.Power => "W",
        SensorKind.Voltage => "V",
        SensorKind.Data => "GB",
        _ => string.Empty
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _computer?.Close();
            }
            catch
            {
                // ignored on shutdown
            }
            finally
            {
                _computer = null;
            }
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
            {
                sub.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
