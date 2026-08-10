using PatchGuard.Models;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class CouncilEvaluatorTests
{
    private readonly CouncilEvaluator _evaluator = new();

    [Fact]
    public void CompleteStepsScoreAsFullyActionable()
    {
        var metrics = _evaluator.Evaluate(CreateGuide(
        [
            new FixStep
            {
                Order = 1,
                Title = "Restart service",
                Instructions = "Restart the Windows Update service from Services and confirm it returns to Running.",
                CopyText = "services.msc"
            }
        ]));

        Assert.Equal(100, metrics.ActionabilityScore);
        Assert.Equal(100, metrics.ConsistencyScore);
    }

    [Fact]
    public void MissingSupportArtifactsReduceActionability()
    {
        var metrics = _evaluator.Evaluate(CreateGuide(
        [
            new FixStep
            {
                Order = 1,
                Title = "Review event log",
                Instructions = "Open Event Viewer and compare the latest warnings against the last healthy baseline."
            }
        ]));

        Assert.Equal(66.7, metrics.ActionabilityScore);
    }

    [Fact]
    public void DuplicateTitlesReduceConsistency()
    {
        var metrics = _evaluator.Evaluate(CreateGuide(
        [
            new FixStep
            {
                Order = 1,
                Title = "Restart service",
                Instructions = "Restart the Windows Update service from Services and confirm it returns to Running.",
                CopyText = "services.msc"
            },
            new FixStep
            {
                Order = 2,
                Title = "Restart service",
                Instructions = "Restart Background Intelligent Transfer Service and confirm it returns to Running.",
                CopyText = "services.msc"
            }
        ]));

        Assert.Equal(83.3, metrics.ConsistencyScore);
    }

    [Fact]
    public void MissingDiscussionAndLocalSourceReduceConsistency()
    {
        var guide = new RepairGuide
        {
            Summary = "Guidance ready.",
            ChiefVerdict = "Use the linked fix first.",
            Sources = [GuidanceSource.WebSourced],
            WebReferences =
            [
                new WebReference
                {
                    Title = "Vendor note",
                    Url = "https://support.example.com/update-fix",
                    Domain = "support.example.com",
                    UsedFor = "Update service recovery"
                }
            ],
            Steps =
            [
                new FixStep
                {
                    Order = 1,
                    Title = "Open support article",
                    Instructions = "Read the vendor article and follow the documented recovery sequence before rebooting again.",
                    LinkUrl = "https://support.example.com/update-fix"
                }
            ]
        };

        var metrics = _evaluator.Evaluate(guide);

        Assert.Equal(66.7, metrics.ConsistencyScore);
    }

    private static RepairGuide CreateGuide(IReadOnlyList<FixStep> steps) =>
        new()
        {
            Summary = "Guidance ready.",
            ChiefVerdict = "Apply the validated recovery steps in order.",
            Sources = [GuidanceSource.Local],
            CouncilDiscussion =
            [
                new CouncilMessage
                {
                    AgentRole = "Technician",
                    Content = "Measured evidence supports a service-side repair.",
                    Headline = "Service issue",
                    Phase = CouncilPhaseType.Analysis,
                    Round = 1,
                    Confidence = 74
                }
            ],
            Steps = steps
        };
}
