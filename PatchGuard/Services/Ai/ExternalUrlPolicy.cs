using System.Net;
using System.Net.Sockets;

namespace PatchGuard.Services.Ai;

public static class ExternalUrlPolicy
{
    private static readonly string[] SpecialUseDnsSuffixes =
    [
        "localhost",
        "local",
        "internal",
        "test",
        "invalid",
        "example",
        "home.arpa",
        "onion"
    ];

    public static bool TryNormalize(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            HasUserInfoDelimiter(value) ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !IsValidPublicHost(candidate))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsValidPublicHost(Uri uri)
    {
        if (IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return !IsNonPublicAddress(address);
        }

        return uri.HostNameType == UriHostNameType.Dns &&
               IsValidPublicFqdn(uri.IdnHost);
    }

    private static bool IsValidPublicFqdn(string host)
    {
        if (host.Length is 0 or > 253)
        {
            return false;
        }

        var labels = host.Split('.');
        if (labels.Length < 2 ||
            labels.Any(static label =>
                label.Length is 0 or > 63 ||
                !char.IsAsciiLetterOrDigit(label[0]) ||
                !char.IsAsciiLetterOrDigit(label[^1]) ||
                label.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            return false;
        }

        return !SpecialUseDnsSuffixes.Any(suffix =>
            string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasUserInfoDelimiter(string value)
    {
        var authorityStart = value.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        var at = value.IndexOf('@', authorityStart);
        if (at < 0)
        {
            return false;
        }

        return IsBeforeDelimiter(value, at, authorityStart, '/') &&
               IsBeforeDelimiter(value, at, authorityStart, '\\') &&
               IsBeforeDelimiter(value, at, authorityStart, '?') &&
               IsBeforeDelimiter(value, at, authorityStart, '#');
    }

    private static bool IsBeforeDelimiter(string value, int position, int start, char delimiter)
    {
        var delimiterPosition = value.IndexOf(delimiter, start);
        return delimiterPosition < 0 || position < delimiterPosition;
    }

    private static bool IsNonPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is 0 or 10 or 127 ||
                   bytes[0] >= 224 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) ||
                   (bytes[0] == 198 && bytes[1] is 18 or 19) ||
                   (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                   (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        var ipv6 = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6Any) ||
               address.Equals(IPAddress.IPv6Loopback) ||
               address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.IsIPv6Multicast ||
               (ipv6[0] & 0xFE) == 0xFC ||
               (ipv6[0] & 0xE0) != 0x20 ||
               (ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0D && ipv6[3] == 0xB8);
    }
}
