using System.Diagnostics.Eventing.Reader;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class EventLogDiagnosticModule : IDiagnosticModule
{
    private const int MaxEvents = 15;
    private static readonly TimeSpan Lookback = TimeSpan.FromHours(48);

    public string Name => "Event Log";
    public string Description => "Scans recent System and Application errors (last 48 hours).";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        foreach (var logName in new[] { "System", "Application" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            findings.AddRange(ReadErrors(logName, cancellationToken));
        }

        if (findings.Count == 0)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "No recent critical errors",
                Details = $"No Error/Critical events in System or Application logs within the last {Lookback.TotalHours:F0} hours.",
                Severity = FindingSeverity.Info,
                Evidence = $"System and Application event queries returned no Level 1/2 records newer than {DateTime.UtcNow - Lookback:u}.",
                ActionState = FindingActionState.None,
                AdminRequirement = FindingAdminRequirement.NotRequired,
                Risk = FindingRisk.NotApplicable,
                VerificationStatus = FindingVerificationStatus.NotRequired
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static IEnumerable<Finding> ReadErrors(string logName, CancellationToken cancellationToken)
    {
        var results = new List<Finding>();
        var cutoff = DateTime.UtcNow - Lookback;

        try
        {
            var query = new EventLogQuery(logName, PathType.LogName, "*[System[(Level=1 or Level=2)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            EventRecord? record;

            while ((record = reader.ReadEvent()) is not null && results.Count < MaxEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (record)
                {
                    var created = record.TimeCreated?.ToUniversalTime();
                    if (created < cutoff)
                    {
                        break;
                    }

                    var id = record.Id;
                    var provider = record.ProviderName ?? "Unknown";
                    var message = Truncate(record.FormatDescription() ?? "(no description)", 200);

                    results.Add(new Finding
                    {
                        ModuleName = "Event Log",
                        Title = $"{logName} Event ID {id}",
                        Details = $"{provider}: {message}",
                        Severity = record.Level <= 1 ? FindingSeverity.Critical : FindingSeverity.Warning,
                        Evidence = $"{logName} log recorded Level {record.Level} Event ID {id} from {provider} at {created:u}: {message}",
                        Recommendation = "Correlate the event time with an observed crash or failure before changing system settings; search the provider and event ID if the issue repeats.",
                        ActionState = FindingActionState.Recommended,
                        AdminRequirement = FindingAdminRequirement.NotRequired,
                        Risk = FindingRisk.Low,
                        VerificationStatus = FindingVerificationStatus.NotVerified
                    });
                }
            }
        }
        catch (Exception ex)
        {
            results.Add(new Finding
            {
                ModuleName = "Event Log",
                Title = $"{logName} log unavailable",
                Details = ex.Message,
                Severity = FindingSeverity.Warning,
                Evidence = $"{logName} event query failed with {ex.GetType().Name}: {ex.Message}",
                ActionState = FindingActionState.Unavailable,
                AdminRequirement = FindingAdminRequirement.Unknown,
                Risk = FindingRisk.Unknown,
                VerificationStatus = FindingVerificationStatus.NotVerified
            });
        }

        return results;
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        return text[..max].TrimEnd() + "…";
    }
}
