using System.Text.RegularExpressions;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class LocalKnowledgeBase
{
    public static string GetTechnicianOpinion(Finding finding)
    {
        if (finding.ModuleName == "Render test")
        {
            return finding.Severity >= FindingSeverity.Warning
                ? "Render score dropped — check GPU driver, close background apps, retest same resolution."
                : "Render score stable — log in-game FPS to confirm.";
        }

        if (finding.Title.Contains("disk", StringComparison.OrdinalIgnoreCase))
        {
            return "Free disk space via Storage Sense; uninstall unused apps.";
        }

        if (finding.ModuleName == "Update services" && finding.Details.Contains("not running", StringComparison.OrdinalIgnoreCase))
        {
            return "Open services.msc — starting services needs admin.";
        }

        if (finding.ModuleName == "Event Log")
        {
            return ExtractEventId(finding.Title) is 10016 or 10010
                ? "DCOM noise — ignore unless apps crash."
                : "Match event time to last change; fix only if it repeats.";
        }

        if (finding.ModuleName == "Memory" && finding.Severity >= FindingSeverity.Warning)
        {
            return "Close heavy apps before gaming.";
        }

        if (finding.Severity == FindingSeverity.Info)
        {
            return "No action — keep as baseline.";
        }

        return "Recheck after one reboot.";
    }

    public static string GetSkepticOpinion(Finding finding)
    {
        if (finding.ModuleName == "Render test")
        {
            return "Synthetic test ≠ game FPS — always log real game numbers too.";
        }

        if (finding.Severity == FindingSeverity.Info)
        {
            return "Info only — skip.";
        }

        if (finding.ModuleName == "Event Log" && ExtractEventId(finding.Title) is 10016 or 10010)
        {
            return "Not actionable.";
        }

        return "Manual steps only.";
    }

    public static string GetResearcherOpinion(Finding finding, IReadOnlyList<WebSearchResult> webResults)
    {
        if (webResults.Count > 0)
        {
            return Trim(webResults[0].Snippet, 90);
        }

        if (finding.ModuleName == "Render test")
        {
            return "Driver updates often move synthetic and game FPS together.";
        }

        return "No web data — use scan numbers.";
    }

    private static int ExtractEventId(string title)
    {
        var match = Regex.Match(title, @"Event ID (\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var id) ? id : 0;
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
