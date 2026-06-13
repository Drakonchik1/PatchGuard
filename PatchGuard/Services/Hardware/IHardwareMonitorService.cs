using PatchGuard.Models;

namespace PatchGuard.Services.Hardware;

public interface IHardwareMonitorService : IDisposable
{
    /// <summary>
    /// Polls all hardware once and returns a fresh snapshot. Safe to call
    /// repeatedly (for example from a UI timer); never throws.
    /// </summary>
    HardwareSnapshot Capture();
}
