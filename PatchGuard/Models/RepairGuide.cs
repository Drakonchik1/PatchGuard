namespace PatchGuard.Models;

public sealed class RepairGuide
{
    public required string Summary { get; init; }
    public required string ChiefVerdict { get; init; }
    public int HealthScore { get; init; }
    public IReadOnlyList<CouncilMessage> CouncilDiscussion { get; init; } = [];
    public IReadOnlyList<FixStep> Steps { get; init; } = [];
    public IReadOnlyList<WebReference> WebReferences { get; init; } = [];
}

public sealed class WebReference
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string UsedFor { get; init; }
}
