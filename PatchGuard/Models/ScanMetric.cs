namespace PatchGuard.Models;

public sealed class ScanMetric
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public int BarPercent { get; init; }
    public FindingSeverity Severity { get; init; } = FindingSeverity.Info;
}
