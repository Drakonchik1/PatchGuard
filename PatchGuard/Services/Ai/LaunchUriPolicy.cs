namespace PatchGuard.Services.Ai;

/// <summary>
/// Validates URIs before opening them from the UI. Allows http(s) web links and
/// a narrow ms-settings: page prefix for built-in Windows Settings shortcuts.
/// </summary>
public static class LaunchUriPolicy
{
    private const string SettingsScheme = "ms-settings";

    public static bool TryNormalize(string? value, out string? launchUri)
    {
        launchUri = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (ExternalUrlPolicy.TryNormalize(trimmed, out var webUri) && webUri is not null)
        {
            launchUri = webUri.AbsoluteUri;
            return true;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var candidate) ||
            !string.Equals(candidate.Scheme, SettingsScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var page = GetSettingsPage(trimmed);
        if (page is null || !IsSimpleSettingsPage(page))
        {
            return false;
        }

        launchUri = $"{SettingsScheme}:{page}";
        return true;
    }

    private static string? GetSettingsPage(string value)
    {
        const string prefix = "ms-settings:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var page = value[prefix.Length..];
        return string.IsNullOrWhiteSpace(page) ? null : page;
    }

    private static bool IsSimpleSettingsPage(string page) =>
        page.Length is > 0 and <= 64 &&
        page.All(static c => char.IsAsciiLetterLower(c) || char.IsDigit(c) || c == '-');
}
