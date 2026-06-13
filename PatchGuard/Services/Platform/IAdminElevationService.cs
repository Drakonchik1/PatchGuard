namespace PatchGuard.Services.Platform;

public interface IAdminElevationService
{
    /// <summary>True when the current process is running with administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Relaunches PatchGuard with a UAC elevation prompt and shuts the current
    /// instance down. Returns false if the user declined the prompt or relaunch
    /// failed (in which case the current instance keeps running unchanged).
    /// </summary>
    bool RestartElevated();
}
