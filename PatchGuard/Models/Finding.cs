namespace PatchGuard.Models;

public sealed class Finding
{
    public required string ModuleName { get; init; }
    public required string Title { get; init; }
    public required string Details { get; init; }
    public FindingSeverity Severity { get; init; } = FindingSeverity.Info;
    public string? Recommendation { get; init; }
}
