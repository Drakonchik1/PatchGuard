using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class LaunchUriPolicyTests
{
    [Theory]
    [InlineData("https://example.com/path", true)]
    [InlineData("http://example.com/path", false)]
    [InlineData("https://localhost/path", false)]
    [InlineData("https://127.0.0.1/path", false)]
    [InlineData("https://10.0.0.1/path", false)]
    [InlineData("https://169.254.1.1/path", false)]
    [InlineData("https://[::1]/path", false)]
    [InlineData("https://[fe80::1]/path", false)]
    [InlineData("ms-settings:storagesense", true)]
    [InlineData("ms-settings:windowsupdate", true)]
    [InlineData("ms-settings:about", true)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:../evil", false)]
    [InlineData("ms-settings:launch?mode=attack", false)]
    [InlineData("https://user:password@example.com", false)]
    public void LaunchPolicyAllowsOnlySafeTargets(string url, bool expected) =>
        Assert.Equal(expected, LaunchUriPolicy.TryNormalize(url, out _));

    [Fact]
    public void SettingsLaunchUriPreservesCanonicalPageName()
    {
        Assert.True(LaunchUriPolicy.TryNormalize("ms-settings:storagesense", out var launchUri));
        Assert.Equal("ms-settings:storagesense", launchUri);
    }
}
