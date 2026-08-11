namespace PatchGuard.Models;

public sealed class FixStep
{
    public int Order { get; init; }
    public required string Title { get; init; }
    public required string Instructions { get; init; }
    public string? CopyText { get; init; }
    public string? LinkUrl { get; init; }

    /// <summary>Why this step matters for the user's machine.</summary>
    public string? WhyThisMatters { get; init; }

    /// <summary>Scan / KB / tool evidence that supports the step.</summary>
    public string? Evidence { get; init; }
}
