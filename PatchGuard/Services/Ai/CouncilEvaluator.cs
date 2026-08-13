using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public sealed class CouncilEvaluator
{
    public CouncilEvaluationMetrics Evaluate(RepairGuide guide)
    {
        return new CouncilEvaluationMetrics
        {
            ActionabilityScore = EvaluateActionability(guide),
            ConsistencyScore = EvaluateConsistency(guide)
        };
    }

    private static double EvaluateActionability(RepairGuide guide)
    {
        if (guide.Steps.Count == 0)
        {
            return string.IsNullOrWhiteSpace(guide.Summary) || string.IsNullOrWhiteSpace(guide.ChiefVerdict)
                ? 0
                : 100;
        }

        var total = guide.Steps.Sum(step =>
        {
            var checks = 0;
            if (!string.IsNullOrWhiteSpace(step.Title))
            {
                checks++;
            }

            if (!string.IsNullOrWhiteSpace(step.Instructions) && step.Instructions.Trim().Length >= 24)
            {
                checks++;
            }

            if (!string.IsNullOrWhiteSpace(step.LinkUrl) || !string.IsNullOrWhiteSpace(step.CopyText))
            {
                checks++;
            }

            return checks / 3d;
        });

        return Math.Round(total / guide.Steps.Count * 100, 1);
    }

    private static double EvaluateConsistency(RepairGuide guide)
    {
        var checks = new[]
        {
            !string.IsNullOrWhiteSpace(guide.Summary),
            !string.IsNullOrWhiteSpace(guide.ChiefVerdict),
            guide.Sources.Contains(GuidanceSource.Local),
            guide.CouncilDiscussion.Count > 0,
            HasDistinctStepTitles(guide),
            HasCoherentProvenance(guide)
        };

        return Math.Round(checks.Count(result => result) / (double)checks.Length * 100, 1);
    }

    private static bool HasDistinctStepTitles(RepairGuide guide)
    {
        var titles = guide.Steps
            .Select(step => step.Title.Trim())
            .Where(title => title.Length > 0)
            .ToList();

        return titles.Count == titles.Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }

    private static bool HasCoherentProvenance(RepairGuide guide)
    {
        var hasWebArtifacts = guide.WebReferences.Count > 0 ||
                              guide.Steps.Any(step => !string.IsNullOrWhiteSpace(step.LinkUrl));
        var hasWebSource = guide.Sources.Contains(GuidanceSource.WebSourced);
        if (hasWebArtifacts != hasWebSource)
        {
            return false;
        }

        var hasKbArtifacts = guide.KnowledgeReferences.Count > 0;
        var hasKbSource = guide.Sources.Contains(GuidanceSource.KnowledgeBase);
        if (hasKbArtifacts != hasKbSource)
        {
            return false;
        }

        var linksAreSafe = guide.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.LinkUrl))
            .All(step => LaunchUriPolicy.TryNormalize(step.LinkUrl!, out _));
        var referencesAreSafe = guide.WebReferences
            .All(reference => ExternalUrlPolicy.TryNormalize(reference.Url, out _));

        return linksAreSafe && referencesAreSafe;
    }
}
