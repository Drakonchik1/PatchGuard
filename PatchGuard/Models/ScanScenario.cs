namespace PatchGuard.Models;

public enum ScanScenario
{
    AfterWindowsUpdate,
    QuickHealthCheck,
    FullSystemAudit,
    GamePerformanceCheck
}

public static class ScanScenarioExtensions
{
    public static string GetTitle(this ScanScenario scenario) => scenario switch
    {
        ScanScenario.AfterWindowsUpdate => "After Windows Update",
        ScanScenario.QuickHealthCheck => "Quick health check",
        ScanScenario.FullSystemAudit => "Full system audit",
        ScanScenario.GamePerformanceCheck => "Game performance check",
        _ => scenario.ToString()
    };

    public static string GetDescription(this ScanScenario scenario) => scenario switch
    {
        ScanScenario.AfterWindowsUpdate =>
            "Scan for common post-update issues: disk space, services, recent errors in logs.",
        ScanScenario.QuickHealthCheck =>
            "Lightweight system overview — OS build, free disk space, and baseline status.",
        ScanScenario.FullSystemAudit =>
            "Everything: OS, disk, services, event logs, temperatures, CPU/GPU, and memory.",
        ScanScenario.GamePerformanceCheck =>
            "Hardware-focused: temperatures, GPU and driver, CPU load, and available memory for gaming.",
        _ => string.Empty
    };
}
