using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class GuidanceSourceBuilder
{
    public static IReadOnlyList<GuidanceSource> Build(
        bool hasAi,
        bool hasWeb,
        bool hasKnowledgeBase)
    {
        var sources = new List<GuidanceSource> { GuidanceSource.Local };
        if (hasAi)
        {
            sources.Add(GuidanceSource.AiGenerated);
        }

        if (hasWeb)
        {
            sources.Add(GuidanceSource.WebSourced);
        }

        if (hasKnowledgeBase)
        {
            sources.Add(GuidanceSource.KnowledgeBase);
        }

        return sources;
    }
}
