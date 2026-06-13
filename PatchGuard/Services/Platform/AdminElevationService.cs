using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace PatchGuard.Services.Platform;

public sealed class AdminElevationService : IAdminElevationService
{
    private readonly Lazy<bool> _isElevated = new(DetectElevation);

    public bool IsElevated => _isElevated.Value;

    public bool RestartElevated()
    {
        if (IsElevated)
        {
            return true;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            // User declined the UAC prompt (ERROR_CANCELLED) or it failed.
            return false;
        }

        Application.Current?.Shutdown();
        return true;
    }

    private static bool DetectElevation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
