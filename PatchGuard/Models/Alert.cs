namespace PatchGuard.Models;

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public sealed class Alert
{
    public required string Id { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string Metric { get; init; }
    public required double Value { get; init; }
    public required double Threshold { get; init; }
    public required string Message { get; init; }
    public required string RecommendedAction { get; init; }
}
