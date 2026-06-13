namespace PatchGuard.Models;

public enum OptimizationStatus
{
    Pending,
    Running,
    Success,
    Skipped,
    Failed
}

public sealed class OptimizationStepResult
{
    public required string StepName { get; init; }
    public OptimizationStatus Status { get; set; } = OptimizationStatus.Pending;
    public string Detail { get; set; } = string.Empty;
    public long BytesFreed { get; set; }

    public string FreedDisplay => BytesFreed > 0 ? FormatBytes(BytesFreed) : "—";

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
