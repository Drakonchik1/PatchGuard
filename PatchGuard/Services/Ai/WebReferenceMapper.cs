using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

public static class WebReferenceMapper
{
    public static IReadOnlyList<WebReference> FromSearchBundles(
        IReadOnlyList<(string Query, IReadOnlyList<WebSearchResult> Results)> bundles)
    {
        var references = new List<WebReference>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (query, results) in bundles)
        {
            foreach (var result in results)
            {
                if (!ExternalUrlPolicy.TryNormalize(result.Url, out var uri) ||
                    uri is null ||
                    !seenUrls.Add(uri.AbsoluteUri))
                {
                    continue;
                }

                references.Add(new WebReference
                {
                    Title = string.IsNullOrWhiteSpace(result.Title)
                        ? uri.Host
                        : result.Title.Trim(),
                    Url = uri.AbsoluteUri,
                    Domain = uri.Host,
                    UsedFor = query
                });
            }
        }

        return references;
    }
}
