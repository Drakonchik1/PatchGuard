namespace PatchGuard.Models;

public sealed class FixStep
{
    public int Order { get; init; }
    public required string Title { get; init; }
    public required string Instructions { get; init; }
    public string? CopyText { get; init; }
    public string? LinkUrl { get; init; }
}
