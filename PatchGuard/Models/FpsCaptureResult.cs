namespace PatchGuard.Models;

public sealed class GameProcessInfo
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string WindowTitle { get; init; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(WindowTitle)
        ? $"{ProcessName} (PID {ProcessId})"
        : $"{ProcessName} — {WindowTitle} (PID {ProcessId})";
}

public sealed class FpsCaptureResult
{
    public required string ProcessName { get; init; }
    public bool Success { get; init; }
    public double AverageFps { get; init; }
    public double OnePercentLowFps { get; init; }
    public double PointOnePercentLowFps { get; init; }
    public int FrameCount { get; init; }
    public double DurationSeconds { get; init; }
    public DateTime CapturedAt { get; init; } = DateTime.Now;
    public string Message { get; init; } = string.Empty;

    public static FpsCaptureResult Failed(string processName, string message) => new()
    {
        ProcessName = processName,
        Success = false,
        Message = message
    };
}
