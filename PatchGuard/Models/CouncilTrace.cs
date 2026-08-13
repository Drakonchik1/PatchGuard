namespace PatchGuard.Models;

/// <summary>
/// Inspectable agent-graph provenance for the Guide UI (nodes, tools, timing).
/// </summary>
public sealed class CouncilTrace
{
    public IReadOnlyList<string> NodesVisited { get; init; } = [];
    public IReadOnlyList<string> ToolsCalled { get; init; } = [];
    public IReadOnlyList<CouncilTraceNodeTiming> NodeTimings { get; init; } = [];
    public int VerifyRetryCount { get; init; }
    public IReadOnlyList<string> RejectedStepReasons { get; init; } = [];
    public long TotalDurationMs { get; init; }
}

public sealed class CouncilTraceNodeTiming
{
    public required string Node { get; init; }
    public long DurationMs { get; init; }
}
