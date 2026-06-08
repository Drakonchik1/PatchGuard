using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class CouncilAgents
{
    public const string Technician = "Technician";
    public const string Skeptic = "Skeptic";
    public const string Researcher = "Researcher";
    public const string ChiefCouncilor = "Chief Councilor";

    public static readonly IReadOnlyList<string> Debaters = [Technician, Skeptic, Researcher];

    public static string GetSystemPrompt(string role) => role switch
    {
        Technician =>
            "Windows technician. Short facts only. Concrete manual steps. No admin. No 'google it'. Max 60 words.",
        Skeptic =>
            "Challenge weak fixes. No admin/registry/scripts. Max 50 words.",
        Researcher =>
            "Use web snippets briefly. Your conclusion in plain words. Max 50 words.",
        ChiefCouncilor =>
            """
            Chief Councilor. JSON only:
            {"summary":"one line","verdict":"2 short paragraphs","healthScore":80,"steps":[{"title":"","instructions":"","linkUrl":null,"copyText":null}]}
            No links to Microsoft support. Max 4 steps.
            """,
        _ => "Be brief and factual."
    };

    public static string GetPhasePrompt(string role, CouncilPhaseType phase) =>
        $"{phase}: {role} — answer from scan data only.";
}
