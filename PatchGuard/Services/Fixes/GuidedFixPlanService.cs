using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Optimization;

namespace PatchGuard.Services.Fixes;

public sealed class GuidedFixPlanService : IGuidedFixPlanService
{
    public static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromMinutes(2);

    private readonly IReadOnlyDictionary<string, IOptimizationStep> _safeSteps;
    private readonly IDbContextFactory<PatchGuardDbContext> _dbContextFactory;
    private readonly TimeSpan _executionTimeout;
    private readonly Func<string, bool> _launchUri;

    public GuidedFixPlanService(
        IEnumerable<IOptimizationStep> optimizationSteps,
        IDbContextFactory<PatchGuardDbContext> dbContextFactory,
        TimeSpan? executionTimeout = null,
        Func<string, bool>? launchUri = null)
    {
        // Optional steps (Explorer restart) are never part of guided fixes.
        _safeSteps = optimizationSteps
            .Where(s => !s.IsOptional)
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _dbContextFactory = dbContextFactory;
        _executionTimeout = executionTimeout ?? DefaultExecutionTimeout;
        _launchUri = launchUri ?? LaunchSettingsOrWebUri;
    }

    public GuidedFixPlan? TryBuildFromFinding(Finding finding, string? linkedScanScenario = null)
    {
        if (finding.ActionState != FindingActionState.Recommended)
        {
            return null;
        }

        // Privileged diagnostics stay manual — guided fixes only run safe, policy-approved actions.
        if (finding.AdminRequirement == FindingAdminRequirement.Required)
        {
            return null;
        }

        var steps = new List<GuidedFixPlanStep>();
        switch (finding.ModuleName)
        {
            case "Memory":
            case "CPU load":
                TryAddOptimizationStep(steps, "Free up RAM (working sets)");
                break;
            case "Disk space":
                TryAddOptimizationStep(steps, "Clear temporary files");
                TryAddOptimizationStep(steps, "Empty Recycle Bin");
                TryAddLaunchStep(steps, "ms-settings:storagesense", "Open Storage Sense",
                    "Opens Windows Storage Sense so you can free more space.");
                break;
            default:
                return null;
        }

        if (steps.Count == 0)
        {
            return null;
        }

        return new GuidedFixPlan
        {
            Id = Truncate($"finding-{SanitizeId(finding.ModuleName)}-{Guid.NewGuid():N}", 48),
            Title = $"Safe fix: {finding.Title}",
            Source = $"Finding:{finding.ModuleName}",
            Steps = steps,
            LinkedScanScenario = linkedScanScenario
        };
    }

    public GuidedFixPlan? TryBuildFromFixStep(FixStep step, string? linkedScanScenario = null)
    {
        var steps = new List<GuidedFixPlanStep>();

        if (LaunchUriPolicy.TryNormalize(step.LinkUrl, out var launchUri) && launchUri is not null)
        {
            TryAddLaunchStep(steps, launchUri, $"Open: {step.Title}", step.Instructions);
        }

        var haystack = $"{step.Title} {step.Instructions}";
        if (ContainsAny(haystack, "temp file", "temporary file", "clear temp", "disk cleanup", "free up space"))
        {
            TryAddOptimizationStep(steps, "Clear temporary files");
            TryAddOptimizationStep(steps, "Empty Recycle Bin");
        }

        if (ContainsAny(haystack, "ram", "memory", "working set"))
        {
            TryAddOptimizationStep(steps, "Free up RAM (working sets)");
        }

        if (ContainsAny(haystack, "dns", "flush dns", "name resolution"))
        {
            TryAddOptimizationStep(steps, "Flush DNS cache");
        }

        if (ContainsAny(haystack, "recycle bin"))
        {
            TryAddOptimizationStep(steps, "Empty Recycle Bin");
        }

        if (steps.Count == 0)
        {
            return null;
        }

        return new GuidedFixPlan
        {
            Id = Truncate($"guide-{step.Order}-{Guid.NewGuid():N}", 48),
            Title = $"Safe fix: {step.Title}",
            Source = $"GuideStep:{step.Order}",
            Steps = steps,
            LinkedScanScenario = linkedScanScenario
        };
    }

    public GuidedFixPreview Preview(GuidedFixPlan plan)
    {
        var summaries = plan.Steps
            .Select(s => $"{s.Title} — {s.Description} (risk: {s.Risk})")
            .ToList();

        return new GuidedFixPreview
        {
            Plan = plan,
            Summary =
                $"Preview {plan.Steps.Count} safe step(s). Nothing runs until you confirm. " +
                $"Overall risk: {plan.OverallRisk}. Admin required: {plan.AdminRequirement}.",
            StepSummaries = summaries,
            Risk = plan.OverallRisk,
            AdminRequirement = plan.AdminRequirement
        };
    }

    public async Task<GuidedFixRunResult> ExecuteAsync(
        GuidedFixPlan plan,
        GuidedFixConfirmation confirmation,
        IProgress<OptimizationStepResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!confirmation.IsConfirmed)
        {
            return new GuidedFixRunResult
            {
                Outcome = GuidedFixOutcome.RejectedNotConfirmed,
                Summary = "Fix was not confirmed. No system changes were made."
            };
        }

        if (plan.Steps.Count == 0)
        {
            return new GuidedFixRunResult
            {
                Outcome = GuidedFixOutcome.Failed,
                Summary = "Fix plan has no executable steps."
            };
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_executionTimeout);
        var token = timeoutCts.Token;

        var results = new List<OptimizationStepResult>();
        try
        {
            foreach (var step in plan.Steps)
            {
                token.ThrowIfCancellationRequested();

                var running = new OptimizationStepResult
                {
                    StepName = step.Title,
                    Status = OptimizationStatus.Running,
                    Detail = step.Description
                };
                progress?.Report(running);

                OptimizationStepResult result;
                try
                {
                    result = await RunStepAsync(step, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result = new OptimizationStepResult
                    {
                        StepName = step.Title,
                        Status = OptimizationStatus.Failed,
                        Detail = ex.Message
                    };
                }

                results.Add(result);
                progress?.Report(result);
            }
        }
        catch (OperationCanceledException)
        {
            var cancelled = BuildResult(
                GuidedFixOutcome.Cancelled,
                "Fix cancelled before all steps completed.",
                results,
                verified: false);
            await PersistAsync(plan, cancelled, confirmation, CancellationToken.None);
            return cancelled;
        }

        var succeeded = results.Count(r => r.Status == OptimizationStatus.Success);
        var failed = results.Count(r => r.Status == OptimizationStatus.Failed);
        GuidedFixOutcome outcome;
        string summary;
        var verified = false;

        if (failed == 0 && succeeded == results.Count)
        {
            outcome = GuidedFixOutcome.Succeeded;
            verified = true;
            summary = $"All {succeeded} step(s) completed and verified.";
        }
        else if (succeeded > 0)
        {
            outcome = GuidedFixOutcome.PartialFailure;
            summary = $"{succeeded} succeeded, {failed} failed. Review details before retrying.";
        }
        else
        {
            outcome = GuidedFixOutcome.Failed;
            summary = "All guided-fix steps failed. No successful changes were verified.";
        }

        var run = BuildResult(outcome, summary, results, verified);
        await PersistAsync(plan, run, confirmation, CancellationToken.None);
        return run;
    }

    private async Task<OptimizationStepResult> RunStepAsync(
        GuidedFixPlanStep step,
        CancellationToken cancellationToken)
    {
        return step.Kind switch
        {
            GuidedFixActionKind.OptimizationStep => await RunOptimizationAsync(step, cancellationToken),
            GuidedFixActionKind.LaunchUri => RunLaunchUri(step),
            _ => new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Failed,
                Detail = "Unsupported guided-fix action."
            }
        };
    }

    private async Task<OptimizationStepResult> RunOptimizationAsync(
        GuidedFixPlanStep step,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.OptimizationStepName) ||
            !_safeSteps.TryGetValue(step.OptimizationStepName, out var optimizerStep))
        {
            return new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Failed,
                Detail = "Safe optimization step is not available."
            };
        }

        if (optimizerStep.IsOptional)
        {
            return new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Skipped,
                Detail = "Optional/privileged steps are blocked in guided fixes."
            };
        }

        return await optimizerStep.RunAsync(cancellationToken);
    }

    private OptimizationStepResult RunLaunchUri(GuidedFixPlanStep step)
    {
        if (!LaunchUriPolicy.TryNormalize(step.LaunchUri, out var launchUri) || launchUri is null)
        {
            return new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Failed,
                Detail = "Blocked a URI that was not a safe web or Windows Settings address."
            };
        }

        try
        {
            if (!_launchUri(launchUri))
            {
                return new OptimizationStepResult
                {
                    StepName = step.Title,
                    Status = OptimizationStatus.Failed,
                    Detail = "Could not open the approved settings/web link."
                };
            }

            return new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Success,
                Detail = $"Opened {launchUri}"
            };
        }
        catch (Exception ex)
        {
            return new OptimizationStepResult
            {
                StepName = step.Title,
                Status = OptimizationStatus.Failed,
                Detail = ex.Message
            };
        }
    }

    private void TryAddOptimizationStep(List<GuidedFixPlanStep> steps, string stepName)
    {
        if (!_safeSteps.TryGetValue(stepName, out var step))
        {
            return;
        }

        if (steps.Any(s =>
                s.Kind == GuidedFixActionKind.OptimizationStep &&
                string.Equals(s.OptimizationStepName, step.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        steps.Add(new GuidedFixPlanStep
        {
            Id = SanitizeId(step.Name),
            Title = step.Name,
            Description = step.Description,
            Kind = GuidedFixActionKind.OptimizationStep,
            OptimizationStepName = step.Name,
            Risk = FindingRisk.Low,
            AdminRequirement = FindingAdminRequirement.NotRequired
        });
    }

    private static void TryAddLaunchStep(
        List<GuidedFixPlanStep> steps,
        string uri,
        string title,
        string description)
    {
        if (!LaunchUriPolicy.TryNormalize(uri, out var launchUri) || launchUri is null)
        {
            return;
        }

        if (steps.Any(s =>
                s.Kind == GuidedFixActionKind.LaunchUri &&
                string.Equals(s.LaunchUri, launchUri, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        steps.Add(new GuidedFixPlanStep
        {
            Id = SanitizeId(title),
            Title = title,
            Description = description,
            Kind = GuidedFixActionKind.LaunchUri,
            LaunchUri = launchUri,
            Risk = FindingRisk.Low,
            AdminRequirement = FindingAdminRequirement.NotRequired
        });
    }

    private async Task PersistAsync(
        GuidedFixPlan plan,
        GuidedFixRunResult result,
        GuidedFixConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        if (!confirmation.IsConfirmed)
        {
            return;
        }

        try
        {
            await using var dbContext =
                await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.GuidedFixRuns.Add(new GuidedFixRunRecord
            {
                RanAt = DateTime.UtcNow,
                Source = Truncate(plan.Source, 128),
                PlanTitle = Truncate(plan.Title, 256),
                Outcome = result.Outcome.ToString(),
                StepsSucceeded = result.StepResults.Count(r => r.Status == OptimizationStatus.Success),
                StepsTotal = result.StepResults.Count,
                BytesFreed = result.BytesFreed,
                Verified = result.Verified,
                LinkedScanScenario = string.IsNullOrWhiteSpace(plan.LinkedScanScenario)
                    ? null
                    : Truncate(plan.LinkedScanScenario, 64),
                Summary = Truncate(result.Summary, 512)
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // History persistence must not undo a completed fix outcome for the UI.
        }
    }

    private static GuidedFixRunResult BuildResult(
        GuidedFixOutcome outcome,
        string summary,
        IReadOnlyList<OptimizationStepResult> results,
        bool verified) =>
        new()
        {
            Outcome = outcome,
            Summary = summary,
            StepResults = results.ToList(),
            Verified = verified,
            BytesFreed = results.Sum(r => r.BytesFreed)
        };

    private static bool LaunchSettingsOrWebUri(string launchUri)
    {
        Process.Start(new ProcessStartInfo(launchUri) { UseShellExecute = true });
        return true;
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string SanitizeId(string value)
    {
        var chars = value
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
