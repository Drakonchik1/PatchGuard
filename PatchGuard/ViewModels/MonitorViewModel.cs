using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PatchGuard.Models;
using PatchGuard.Services.Hardware;
using PatchGuard.Services.Platform;

namespace PatchGuard.ViewModels;

public partial class MonitorViewModel : ObservableObject, INavigationAware, INavigationLeave
{
    private readonly IHardwareMonitorService _hardware;
    private readonly IAdminElevationService _elevation;
    private readonly DispatcherTimer _timer;

    public MonitorViewModel(IHardwareMonitorService hardware, IAdminElevationService elevation)
    {
        _hardware = hardware;
        _elevation = elevation;
        IsElevated = elevation.IsElevated;

        // 2s strikes a balance between live feedback and a low CPU footprint.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<SensorReading> Sensors { get; } = [];

    [ObservableProperty] private bool _isElevated;
    [ObservableProperty] private bool _sensorsLimited;
    [ObservableProperty] private bool _monitorUnavailable;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private string _cpuName = "CPU";
    [ObservableProperty] private string _cpuTempText = "n/a";
    [ObservableProperty] private string _cpuLoadText = "n/a";
    [ObservableProperty] private double _cpuLoadPercent;
    [ObservableProperty] private double _cpuTempPercent;
    [ObservableProperty] private string _cpuExtraText = string.Empty;

    [ObservableProperty] private string _gpuName = "GPU";
    [ObservableProperty] private string _gpuTempText = "n/a";
    [ObservableProperty] private string _gpuLoadText = "n/a";
    [ObservableProperty] private double _gpuLoadPercent;
    [ObservableProperty] private double _gpuTempPercent;
    [ObservableProperty] private string _gpuExtraText = string.Empty;

    [ObservableProperty] private string _ramText = "n/a";
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramDetailText = string.Empty;

    public void OnNavigatedTo()
    {
        Refresh();
        _timer.Start();
    }

    public void OnNavigatedFrom() => _timer.Stop();

    [RelayCommand]
    private void RunAsAdmin() => _elevation.RestartElevated();

    private void Refresh()
    {
        var s = _hardware.Capture();

        MonitorUnavailable = s.MonitorUnavailable;
        SensorsLimited = s.SensorsLimited;
        StatusMessage = s.StatusMessage;
        IsElevated = _elevation.IsElevated;

        CpuName = s.CpuName;
        CpuTempText = Temp(s.CpuTemperatureC);
        CpuTempPercent = Clamp(s.CpuTemperatureC);
        CpuLoadText = Percent(s.CpuLoadPercent);
        CpuLoadPercent = s.CpuLoadPercent ?? 0;
        CpuExtraText = BuildExtra(
            s.CpuClockMhz is { } mhz ? $"{mhz:F0} MHz" : null,
            s.CpuPowerWatts is { } w ? $"{w:F0} W" : null);

        GpuName = s.GpuName;
        GpuTempText = Temp(s.GpuTemperatureC);
        GpuTempPercent = Clamp(s.GpuTemperatureC);
        GpuLoadText = Percent(s.GpuLoadPercent);
        GpuLoadPercent = s.GpuLoadPercent ?? 0;
        GpuExtraText = BuildExtra(
            s.GpuMemoryUsedMb is { } used && s.GpuMemoryTotalMb is { } total
                ? $"{used / 1024:F1}/{total / 1024:F1} GB VRAM"
                : null,
            s.GpuPowerWatts is { } gw ? $"{gw:F0} W" : null);

        if (s is { RamUsedGb: { } ru, RamTotalGb: { } rt } && rt > 0)
        {
            RamText = $"{ru:F1} / {rt:F1} GB";
            RamPercent = s.RamLoadPercent ?? ru / rt * 100;
            RamDetailText = $"{Math.Max(0, rt - ru):F1} GB free";
        }
        else
        {
            RamText = "n/a";
            RamPercent = 0;
            RamDetailText = string.Empty;
        }

        UpdateSensors(s.Sensors);
    }

    private void UpdateSensors(IReadOnlyList<SensorReading> readings)
    {
        Sensors.Clear();
        foreach (var reading in readings
                     .Where(r => r.Kind is SensorKind.Temperature or SensorKind.Fan or SensorKind.Power)
                     .OrderBy(r => r.Hardware)
                     .ThenBy(r => r.Name))
        {
            Sensors.Add(reading);
        }
    }

    private static string Temp(double? value) => value is { } v ? $"{v:F0} °C" : "n/a";
    private static string Percent(double? value) => value is { } v ? $"{v:F0} %" : "n/a";
    private static double Clamp(double? value) => value is { } v ? Math.Clamp(v, 0, 100) : 0;

    private static string BuildExtra(string? a, string? b)
    {
        var parts = new[] { a, b }.Where(p => !string.IsNullOrEmpty(p));
        return string.Join("   ·   ", parts);
    }
}
