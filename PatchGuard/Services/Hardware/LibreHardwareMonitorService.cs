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
    private readonly IOsThermalTemperatureSource _osThermal;
    private readonly object _gate = new();
    private readonly UpdateVisitor _visitor = new();

    private Computer? _computer;
    private bool _initFailed;
    private bool _disposed;
    private bool _needsWarmup;

    public LibreHardwareMonitorService(
        IAdminElevationService elevation,
        IOsThermalTemperatureSource? osThermal = null)
    {
        _elevation = elevation;
        _osThermal = osThermal ?? new WindowsThermalZoneTemperatureSource();
    }

    public HardwareSnapshot Capture()
    {
        lock (_gate)
        {
            var state = new CaptureState();

            if (_disposed)
            {
                state.Snapshot.MonitorUnavailable = true;
                state.Snapshot.StatusMessage = "Hardware monitor has been shut down.";
                return state.Snapshot;
            }

            if (!TryEnsureComputer(state.Snapshot))
            {
                FillRamFromOs(state.Snapshot);
                return state.Snapshot;
            }

            try
            {
                // ADL / AMD SMU sensors often need several Update passes after Open()
                // before Tctl/Tdie and package power leave the zero stub state.
                _computer!.Accept(_visitor);
                if (_needsWarmup)
                {
                    _computer.Accept(_visitor);
                    _computer.Accept(_visitor);
                    _needsWarmup = false;
                }

                foreach (var hardware in _computer.Hardware)
                {
                    ReadHardware(hardware, state);
                }
            }
            catch (Exception ex)
            {
                state.Snapshot.StatusMessage = $"Sensor read error: {ex.Message}";
            }

            if (state.Snapshot.RamTotalGb is null or 0)
            {
                FillRamFromOs(state.Snapshot);
            }

            ApplyOsThermalFallback(state.Snapshot);
            FinalizeSensorAvailability(state.Snapshot, _elevation.IsElevated);

            return state.Snapshot;
        }
    }

    private void ApplyOsThermalFallback(HardwareSnapshot snapshot)
    {
        if (snapshot.CpuTemperatureC is not null)
        {
            return;
        }

        var osTemp = _osThermal.TryReadCpuTemperatureC();
        if (osTemp is not { } temp || !IsPlausibleTemperature(temp))
        {
            return;
        }

        snapshot.CpuTemperatureC = temp;
        snapshot.Sensors.Add(new SensorReading
        {
            Hardware = "Windows Thermal Zone",
            Name = "CPU (ACPI)",
            Kind = SensorKind.Temperature,
            Value = temp,
            Unit = "°C"
        });
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
                IsControllerEnabled = true,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };
            computer.Open();
            _computer = computer;
            _needsWarmup = true;
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

    private static void ReadHardware(IHardware hardware, CaptureState state)
    {
        var snapshot = state.Snapshot;
        var isCpu = hardware.HardwareType == HardwareType.Cpu;
        var isGpu = hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;
        var isMemory = hardware.HardwareType == HardwareType.Memory;
        var isSecondaryTempHost = hardware.HardwareType is HardwareType.Motherboard
            or HardwareType.Cooler
            or HardwareType.EmbeddedController;
        var gpuPreference = GpuPreference(hardware.HardwareType);

        if (isCpu)
        {
            snapshot.CpuName = hardware.Name;
        }
        else if (isGpu && gpuPreference >= state.GpuPreference)
        {
            // Prefer discrete AMD/NVIDIA over integrated Intel when both exist.
            if (gpuPreference > state.GpuPreference)
            {
                ClearGpuSummary(snapshot);
                state.GpuPreference = gpuPreference;
            }

            snapshot.GpuName = hardware.Name;
        }

        var applyGpuSummary = isGpu && gpuPreference >= state.GpuPreference;

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float raw || float.IsNaN(raw) || float.IsInfinity(raw))
            {
                continue;
            }

            var value = raw;
            var kind = MapKind(sensor.SensorType);
            if (kind is not null && IsDisplayableSensor(kind.Value, value))
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
                ApplyCpu(snapshot, sensor.SensorType, sensor.Name, value);
            }
            else if (applyGpuSummary)
            {
                ApplyGpu(snapshot, sensor.SensorType, sensor.Name, value);
            }
            else if (isMemory)
            {
                ApplyMemory(snapshot, sensor, value);
            }
            else if (isSecondaryTempHost)
            {
                ApplyMotherboardCpuTemp(snapshot, sensor.SensorType, sensor.Name, value);
            }
        }

        foreach (var sub in hardware.SubHardware)
        {
            ReadHardware(sub, state);
        }
    }

    private static void ClearGpuSummary(HardwareSnapshot snapshot)
    {
        snapshot.GpuName = "GPU";
        snapshot.GpuTemperatureC = null;
        snapshot.GpuLoadPercent = null;
        snapshot.GpuMemoryUsedMb = null;
        snapshot.GpuMemoryTotalMb = null;
        snapshot.GpuPowerWatts = null;
    }

    /// <summary>Higher wins when multiple GPUs are present (iGPU + dGPU).</summary>
    public static int GpuPreference(HardwareType type) => type switch
    {
        HardwareType.GpuNvidia => 3,
        HardwareType.GpuAmd => 3,
        HardwareType.GpuIntel => 1,
        _ => 0
    };

    public static void ApplyCpu(HardwareSnapshot snapshot, SensorType sensorType, string sensorName, double value)
    {
        switch (sensorType)
        {
            case SensorType.Temperature when IsPreferredCpuTemp(sensorName):
                if (IsPlausibleTemperature(value))
                {
                    snapshot.CpuTemperatureC = value;
                }
                break;
            case SensorType.Temperature when IsPlausibleTemperature(value):
                // Fall back to the hottest core / CCD if no package sensor is present.
                snapshot.CpuTemperatureC = snapshot.CpuTemperatureC is { } existing
                    ? Math.Max(existing, value)
                    : value;
                break;
            case SensorType.Load when sensorName.Contains("Total", StringComparison.OrdinalIgnoreCase):
                snapshot.CpuLoadPercent = value;
                break;
            case SensorType.Power when IsPlausiblePower(value)
                                       && (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                           || sensorName.Contains("PPT", StringComparison.OrdinalIgnoreCase)
                                           || sensorName.Equals("CPU Cores", StringComparison.OrdinalIgnoreCase)):
                snapshot.CpuPowerWatts = value;
                break;
            case SensorType.Clock when snapshot.CpuClockMhz is null
                                       && value > 0
                                       && sensorName.Contains("Core", StringComparison.OrdinalIgnoreCase):
                snapshot.CpuClockMhz = value;
                break;
        }
    }

    /// <summary>
    /// Hawk Point / newer Ryzen often expose a working CPU temp on the EC/motherboard
    /// while Core (Tctl/Tdie) stays stuck at 0 in LibreHardwareMonitor.
    /// </summary>
    public static void ApplyMotherboardCpuTemp(
        HardwareSnapshot snapshot,
        SensorType sensorType,
        string sensorName,
        double value)
    {
        if (sensorType != SensorType.Temperature
            || !IsPlausibleTemperature(value)
            || !IsMotherboardCpuTempName(sensorName))
        {
            return;
        }

        if (snapshot.CpuTemperatureC is null)
        {
            snapshot.CpuTemperatureC = value;
        }
    }

    /// <summary>
    /// Maps GPU sensors including AMD ADL names (GPU Core / Hot Spot) and
    /// D3D load fallbacks when PMLog core load is unavailable.
    /// </summary>
    public static void ApplyGpu(
        HardwareSnapshot snapshot,
        SensorType sensorType,
        string sensorName,
        double value)
    {
        switch (sensorType)
        {
            case SensorType.Temperature when IsPreferredGpuTemp(sensorName):
                if (IsPlausibleTemperature(value))
                {
                    snapshot.GpuTemperatureC = value;
                }
                break;
            case SensorType.Temperature when snapshot.GpuTemperatureC is null
                                             && IsAcceptableGpuTemp(sensorName)
                                             && IsPlausibleTemperature(value):
                snapshot.GpuTemperatureC = value;
                break;
            case SensorType.Load when IsPreferredGpuLoad(sensorName):
                snapshot.GpuLoadPercent = value;
                break;
            case SensorType.Load when snapshot.GpuLoadPercent is null
                                      && IsFallbackGpuLoad(sensorName):
                snapshot.GpuLoadPercent = value;
                break;
            case SensorType.SmallData when sensorName.Contains("Memory Used", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuMemoryUsedMb = value;
                break;
            case SensorType.SmallData when sensorName.Contains("Memory Total", StringComparison.OrdinalIgnoreCase):
                snapshot.GpuMemoryTotalMb = value;
                break;
            case SensorType.Power when IsPlausiblePower(value)
                                       && (sensorName.Contains("Package", StringComparison.OrdinalIgnoreCase)
                                           || sensorName.Contains("Total", StringComparison.OrdinalIgnoreCase)
                                           || sensorName.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)
                                           || snapshot.GpuPowerWatts is null):
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

    private static void FinalizeSensorAvailability(HardwareSnapshot snapshot, bool isElevated)
    {
        var missingPackageTemps = snapshot is { CpuTemperatureC: null, GpuTemperatureC: null };
        // Keep the "needs admin" meaning for UI/diagnostics; elevated AMD SMU zeros
        // are communicated via StatusMessage instead.
        snapshot.SensorsLimited = missingPackageTemps && !isElevated;

        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            return;
        }

        if (snapshot.CpuTemperatureC is null)
        {
            snapshot.StatusMessage = isElevated
                ? "CPU package temperature unavailable (LibreHardwareMonitor AMD SMU returned 0; Windows thermal zone also unavailable)."
                : "Temperature sensors usually require administrator rights.";
        }
    }

    /// <summary>
    /// LHM often exposes stub zeros for unsupported AMD SMU reads (Tctl/Tdie = 0 °C,
    /// per-core SMU power = 0 W). Those must not become summary values or list noise.
    /// </summary>
    public static bool IsPlausibleTemperature(double value) => value is > 1 and < 125;

    public static bool IsPlausiblePower(double value) => value > 0.05;

    public static bool IsDisplayableSensor(SensorKind kind, double value) => kind switch
    {
        SensorKind.Temperature => IsPlausibleTemperature(value),
        SensorKind.Power => IsPlausiblePower(value),
        SensorKind.Fan => value > 0,
        SensorKind.Clock => value > 0,
        _ => true
    };

    private static bool IsPreferredCpuTemp(string name) =>
        name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CCD", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("CPU", StringComparison.OrdinalIgnoreCase);

    private static bool IsMotherboardCpuTempName(string name) =>
        name.Equals("CPU", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Package", StringComparison.OrdinalIgnoreCase);

    public static bool IsPreferredGpuTemp(string name) =>
        name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains("Core", StringComparison.OrdinalIgnoreCase)
         && !name.Contains("Memory", StringComparison.OrdinalIgnoreCase)
         && !name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)
         && !name.Contains("VR", StringComparison.OrdinalIgnoreCase));

    public static bool IsAcceptableGpuTemp(string name) =>
        name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VR SoC", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Temperature", StringComparison.OrdinalIgnoreCase) ||
        !name.Contains("Memory", StringComparison.OrdinalIgnoreCase);

    public static bool IsPreferredGpuLoad(string name) =>
        name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains("Core", StringComparison.OrdinalIgnoreCase)
         && !name.Contains("Memory", StringComparison.OrdinalIgnoreCase)
         && !name.StartsWith("D3D", StringComparison.OrdinalIgnoreCase));

    public static bool IsFallbackGpuLoad(string name) =>
        name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("3D", StringComparison.OrdinalIgnoreCase);

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

            if (_osThermal is IDisposable disposableThermal)
            {
                disposableThermal.Dispose();
            }
        }
    }

    private sealed class CaptureState
    {
        public HardwareSnapshot Snapshot { get; } = new();
        public int GpuPreference { get; set; }
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
