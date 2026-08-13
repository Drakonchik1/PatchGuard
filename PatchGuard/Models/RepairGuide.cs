namespace PatchGuard.Models;

public sealed class RepairGuide
{
    public required string Summary { get; init; }
    public required string ChiefVerdict { get; init; }

    /// <summary>Longer “why we recommend this” narrative for the Guide UI.</summary>
    public string? DetailedExplanation { get; init; }

    public int HealthScore { get; init; }
    public IReadOnlyList<CouncilMessage> CouncilDiscussion { get; init; } = [];
    public IReadOnlyList<FixStep> Steps { get; init; } = [];
    public IReadOnlyList<WebReference> WebReferences { get; init; } = [];
    public IReadOnlyList<KnowledgeReference> KnowledgeReferences { get; init; } = [];
    public IReadOnlyList<GuidanceSource> Sources { get; init; } = [GuidanceSource.Local];

    /// <summary>OpenAI | Ollama when AI council ran; null for rules-only.</summary>
    public string? AiProviderName { get; init; }

    /// <summary>Agent-graph provenance (nodes, tools, verify retries) when LLM council ran.</summary>
    public CouncilTrace? Trace { get; init; }
}

public enum GuidanceSource
{
    Local,
    AiGenerated,
    WebSourced,
    KnowledgeBase
}

public sealed class WebReference
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Domain { get; init; }
    public required string UsedFor { get; init; }
}

/// <summary>Local playbook citation — no remote URL; shown as inspectable provenance.</summary>
public sealed class KnowledgeReference
{
    public required string Title { get; init; }
    public required string PlaybookId { get; init; }
    public required string UsedFor { get; init; }
    public double Score { get; init; }
}
