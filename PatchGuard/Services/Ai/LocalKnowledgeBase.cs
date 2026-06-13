using System.Text.RegularExpressions;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class LocalKnowledgeBase
{
    public static string GetTechnicianOpinion(Finding finding)
    {
        if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            finding.Details.Contains("GB free", StringComparison.OrdinalIgnoreCase))
        {
            return "Free space is below a safe margin for cumulative updates. I'd clear Storage Sense temp files, uninstall two largest unused apps, and empty Recycle Bin before retrying any update — that alone fixes a large share of failed patches.";
        }

        if (finding.ModuleName == "Update services")
        {
            if (finding.Details.Contains("not running", StringComparison.OrdinalIgnoreCase))
            {
                return "A stopped update service blocks patching. Without admin I can't start it from here — I'd open services.msc, confirm wuauserv/BITS status, and schedule a restart when an admin is available. Until then, pause optional updates.";
            }

            return "Update services look healthy. I'd still note the status now so we can compare if a future scan shows a regression.";
        }

        if (finding.ModuleName == "Event Log")
        {
            var eventId = ExtractEventId(finding.Title);
            return eventId switch
            {
                10016 or 10010 =>
                    "DCOM 10016/10010 noise is extremely common after builds — usually not actionable. I'd only escalate if the same app crashes daily alongside this entry.",
                7000 or 7009 or 7023 =>
                    "A service failed to start — read the provider name in the log line. Match it in services.msc; if it's not Windows Update related, treat it as a separate app issue.",
                6008 =>
                    "Unexpected shutdown logged — check power cable, sleep settings, and whether the PC rebooted during an update. Run this scan again after the next clean boot.",
                41 =>
                    "Kernel-power 41 means the machine lost power or hard-reset. Finish any pending updates, disable fast startup temporarily, and watch if it repeats.",
                _ =>
                    $"Event {eventId} needs context from the provider line. I'd correlate timestamp with the last KB install — if they align, pause driver-heavy apps for 24h and rescan."
            };
        }

        if (finding.ModuleName == "Windows Update history")
        {
            return "I see recent KB activity. If symptoms began after the latest package, I'd record that KB ID and avoid stacking more updates until stability returns — rollback via Settings is an option before chasing ghosts.";
        }

        if (finding.Severity == FindingSeverity.Info)
        {
            return "Baseline looks acceptable. I'd still export today's build number and disk reading — that comparison is our best post-update diagnostic.";
        }

        return $"I'd treat \"{finding.Title}\" as a live signal: validate it twice across reboots before changing system settings.";
    }

    public static string GetSkepticOpinion(Finding finding, string technicianTake)
    {
        if (finding.Severity == FindingSeverity.Info)
        {
            return "No need to over-fit — info-level entries aren't incidents. Don't run aggressive cleaners on a healthy scan.";
        }

        if (finding.ModuleName == "Event Log" && ExtractEventId(finding.Title) is 10016 or 10010)
        {
            return "I disagree that DCOM warnings need action — forums over-treat these. Unless an app fails to open, deprioritize against real disk or service warnings.";
        }

        if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase))
        {
            return "Disk warnings are real — but confirm Storage Sense isn't counting a nearly-full recovery partition. Delete personal files first, not system folders.";
        }

        return $"Technician's take on \"{finding.Title}\" is plausible — I'd still avoid scripts from random repos. Manual steps only, no elevation.";
    }

    public static string GetResearcherOpinion(
        Finding finding,
        IReadOnlyList<WebSearchResult> webResults,
        string queryUsed)
    {
        var match = webResults.FirstOrDefault(r =>
            r.Snippet.Contains(finding.Title, StringComparison.OrdinalIgnoreCase) ||
            (ExtractEventId(finding.Title) > 0 &&
             r.Snippet.Contains(ExtractEventId(finding.Title).ToString(), StringComparison.OrdinalIgnoreCase)));

        if (match is not null)
        {
            return $"For \"{finding.Title}\", external threads point to: {Trim(match.Snippet, 160)}. My read: apply only steps that don't need admin — ignore registry hacks.";
        }

        if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase))
        {
            return "Community pattern: sub-10 GB free causes Update Orchestrator to abort silently. Storage Sense + removing old user Downloads folders is the recurring fix — no forum script required.";
        }

        if (webResults.Count > 0)
        {
            return $"General research on \"{queryUsed}\" suggests: {Trim(webResults[0].Snippet, 150)}. Map that to our exact finding before acting.";
        }

        return $"No web API configured — using internal playbooks for \"{finding.Title}\". Enable Tavily in config for live thread synthesis.";
    }

    public static int EstimateConfidence(Finding finding, bool agreesWithTeam)
    {
        var baseScore = finding.Severity switch
        {
            FindingSeverity.Critical => 85,
            FindingSeverity.Warning => 70,
            _ => 55
        };

        return agreesWithTeam ? baseScore : Math.Max(40, baseScore - 15);
    }

    private static int ExtractEventId(string title)
    {
        var match = Regex.Match(title, @"Event ID (\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : 0;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
