using System.Diagnostics;

namespace PatchGuard.Services.Hardware;

/// <summary>
/// Reads Windows "Thermal Zone Information" performance counters (Kelvin → °C).
/// On ASUS ROG / many AMD laptops this exposes a usable package approximation
/// even when LibreHardwareMonitor Tctl/Tdie stays at 0.
/// </summary>
public sealed class WindowsThermalZoneTemperatureSource : IOsThermalTemperatureSource, IDisposable
{
    private const string CategoryName = "Thermal Zone Information";
    private const string CounterName = "Temperature";

    private readonly object _gate = new();
    private List<PerformanceCounter>? _counters;
    private bool _primed;
    private bool _unavailable;
    private bool _disposed;

    public double? TryReadCpuTemperatureC()
    {
        lock (_gate)
        {
            if (_disposed || _unavailable)
            {
                return null;
            }

            try
            {
                EnsureCounters();
                if (_counters is null || _counters.Count == 0)
                {
                    _unavailable = true;
                    return null;
                }

                // First NextValue() after create is often 0; prime once.
                if (!_primed)
                {
                    foreach (var counter in _counters)
                    {
                        _ = counter.NextValue();
                    }

                    _primed = true;
                    Thread.Sleep(50);
                }

                double? best = null;
                foreach (var counter in _counters)
                {
                    var kelvin = counter.NextValue();
                    var celsius = kelvin - 273.15d;
                    if (!LibreHardwareMonitorService.IsPlausibleTemperature(celsius))
                    {
                        continue;
                    }

                    best = best is { } existing ? Math.Max(existing, celsius) : celsius;
                }

                return best;
            }
            catch
            {
                DisposeCounters();
                _unavailable = true;
                return null;
            }
        }
    }

    private void EnsureCounters()
    {
        if (_counters is not null)
        {
            return;
        }

        if (!PerformanceCounterCategory.Exists(CategoryName))
        {
            _unavailable = true;
            return;
        }

        var category = new PerformanceCounterCategory(CategoryName);
        var counters = new List<PerformanceCounter>();
        foreach (var instance in category.GetInstanceNames())
        {
            counters.Add(new PerformanceCounter(CategoryName, CounterName, instance, readOnly: true));
        }

        _counters = counters;
    }

    private void DisposeCounters()
    {
        if (_counters is null)
        {
            return;
        }

        foreach (var counter in _counters)
        {
            counter.Dispose();
        }

        _counters = null;
        _primed = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeCounters();
        }
    }
}
