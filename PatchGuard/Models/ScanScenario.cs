namespace PatchGuard.Models;

public enum ScanScenario
{
    AfterWindowsUpdate,
    QuickHealthCheck,
    GamePerformanceCheck
}

public static class ScanScenarioExtensions
{
    public static string GetTitle(this ScanScenario scenario) => scenario switch
    {
        ScanScenario.AfterWindowsUpdate => "After Windows Update",
        ScanScenario.QuickHealthCheck => "Quick health check",
        ScanScenario.GamePerformanceCheck => "Game FPS check",
        _ => scenario.ToString()
    };

    public static string GetDescription(this ScanScenario scenario) => scenario switch
    {
        ScanScenario.AfterWindowsUpdate =>
            "Disk, updates, services, event log.",
        ScanScenario.QuickHealthCheck =>
            "OS build and free disk space only.",
        ScanScenario.GamePerformanceCheck =>
            "GPU, RAM load, quick render test, compare FPS you log in-game.",
        _ => string.Empty
    };
}
