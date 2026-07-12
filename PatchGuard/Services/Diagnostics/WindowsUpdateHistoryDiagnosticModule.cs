using System.Management;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class WindowsUpdateHistoryDiagnosticModule : IDiagnosticModule
{
    public string Name => "Windows Update history";
    public string Description => "Lists recently installed KB packages.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");

            var updates = searcher.Get()
                .Cast<ManagementObject>()
                .Select(o => new
                {
                    Id = o["HotFixID"]?.ToString() ?? "Unknown",
                    Description = o["Description"]?.ToString() ?? string.Empty,
                    InstalledOn = ParseDate(o["InstalledOn"])
                })
                .OrderByDescending(u => u.InstalledOn)
                .Take(8)
                .ToList();

            if (updates.Count == 0)
            {
                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = "No update history found",
                    Details = "WMI returned no hotfix records.",
                    Severity = FindingSeverity.Info,
                    Evidence = "Win32_QuickFixEngineering returned zero hotfix records.",
                    ActionState = FindingActionState.Unavailable,
                    AdminRequirement = FindingAdminRequirement.NotRequired,
                    Risk = FindingRisk.Unknown,
                    VerificationStatus = FindingVerificationStatus.NotVerified
                });
            }
            else
            {
                var latest = updates[0];
                findings.Add(new Finding
                {
                    ModuleName = Name,
                    Title = $"Latest: {latest.Id}",
                    Details = string.Join(Environment.NewLine, updates.Select(u =>
                        $"{u.Id} ({u.InstalledOn:yyyy-MM-dd}) — {u.Description}")),
                    Severity = FindingSeverity.Info,
                    Evidence = $"WMI reported {updates.Count} recent hotfix record(s); newest was {latest.Id} installed {latest.InstalledOn:yyyy-MM-dd}.",
                    ActionState = FindingActionState.None,
                    AdminRequirement = FindingAdminRequirement.NotRequired,
                    Risk = FindingRisk.NotApplicable,
                    VerificationStatus = FindingVerificationStatus.NotRequired
                });
            }
        }
        catch (Exception ex)
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Update history check failed",
                Details = ex.Message,
                Severity = FindingSeverity.Warning,
                Evidence = $"Win32_QuickFixEngineering query failed with {ex.GetType().Name}: {ex.Message}",
                ActionState = FindingActionState.Unavailable,
                AdminRequirement = FindingAdminRequirement.Unknown,
                Risk = FindingRisk.Unknown,
                VerificationStatus = FindingVerificationStatus.NotVerified
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static DateTime ParseDate(object? value)
    {
        if (value is null)
        {
            return DateTime.MinValue;
        }

        if (value is DateTime dt)
        {
            return dt;
        }

        return DateTime.TryParse(value.ToString(), out var parsed) ? parsed : DateTime.MinValue;
    }
}
