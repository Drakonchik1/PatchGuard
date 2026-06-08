using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class MemoryLoadDiagnosticModule : IDiagnosticModule
{
    public string Name => "Memory";
    public string Description => "RAM use (affects stutter in games).";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        try
        {
            var availableMb = GetAvailableMemoryMb();
            var severity = availableMb < 2048 ? FindingSeverity.Warning : FindingSeverity.Info;

            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = $"~{availableMb:F0} MB RAM free",
                Details = severity == FindingSeverity.Warning
                    ? "Close heavy apps before gaming."
                    : "RAM OK for gaming.",
                Severity = severity
            });
        }
        catch (Exception ex)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Memory check failed",
                Details = ex.Message,
                Severity = FindingSeverity.Warning
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static double GetAvailableMemoryMb()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref memStatus))
        {
            return 0;
        }

        return memStatus.ullAvailPhys / (1024.0 * 1024);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
