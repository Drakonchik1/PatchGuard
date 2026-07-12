namespace PatchGuard.Services.Ai;

public static class ExternalUrlPolicy
{
    public static bool TryNormalize(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            (candidate.Scheme != Uri.UriSchemeHttp &&
             candidate.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            string.IsNullOrWhiteSpace(candidate.Host))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
