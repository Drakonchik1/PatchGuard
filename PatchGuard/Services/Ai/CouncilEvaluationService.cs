using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public sealed class CouncilEvaluationService : ICouncilEvaluationService
{
    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;
    private readonly CouncilEvaluator _evaluator;

    public CouncilEvaluationService(
        IDbContextFactory<PatchGuardDbContext> dbContextFactory,
        CouncilEvaluator evaluator)
    {
        _dbContextFactory = dbContextFactory;
        _evaluator = evaluator;
    }

    public async Task SaveAsync(
        ScanScenario scenario,
        RepairGuide guide,
        TimeSpan latency,
        CancellationToken cancellationToken = default)
    {
        var metrics = _evaluator.Evaluate(guide);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.CouncilEvaluations.Add(new CouncilEvaluationRecord
        {
            EvaluatedAt = DateTime.UtcNow,
            Scenario = scenario.ToString(),
            Source = BuildSourceLabel(guide.Sources, guide.AiProviderName),
            LatencyMs = (int)Math.Clamp(latency.TotalMilliseconds, 0, int.MaxValue),
            FixStepCount = guide.Steps.Count,
            CouncilMessageCount = guide.CouncilDiscussion.Count,
            ActionabilityScore = metrics.ActionabilityScore,
            ConsistencyScore = metrics.ConsistencyScore
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CouncilEvaluationRecord>> GetRecentAsync(
        int take = 10,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var records = await dbContext.CouncilEvaluations
            .AsNoTracking()
            .OrderByDescending(record => record.EvaluatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            record.EvaluatedAt = record.EvaluatedAt.ToLocalTime();
        }

        return records;
    }

    internal static string BuildSourceLabel(
        IReadOnlyList<GuidanceSource> sources,
        string? aiProviderName = null)
    {
        var hasAi = sources.Contains(GuidanceSource.AiGenerated);
        var hasWeb = sources.Contains(GuidanceSource.WebSourced);
        var hasKb = sources.Contains(GuidanceSource.KnowledgeBase);
        var aiLabel = aiProviderName?.ToUpperInvariant() switch
        {
            "OLLAMA" => "Ollama",
            "AZURE" => "Azure",
            "OPENAI" => "AI",
            _ => "AI"
        };

        return (hasAi, hasWeb) switch
        {
            (true, true) => hasKb ? $"{aiLabel}+Web+KB" : $"{aiLabel}+Web",
            (true, false) => hasKb ? $"{aiLabel}+KB" : aiLabel,
            (false, true) => hasKb ? "Web+KB" : "Web",
            _ => hasKb ? "Local+KB" : "Local"
        };
    }
}
