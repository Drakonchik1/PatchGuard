using System.Runtime.InteropServices;
using Microsoft.Win32;
using PatchGuard.Models;

namespace PatchGuard.Services.Diagnostics;

public sealed class OsInfoDiagnosticModule : IDiagnosticModule
{
    public string Name => "Operating system";
    public string Description => "Reads Windows version and build number.";
    public bool IsImplemented => true;

    public Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            findings.Add(new Finding
            {
                ModuleName = Name,
                Title = "Non-Windows platform",
                Details = $"PatchGuard starter pack targets Windows. Detected: {RuntimeInformation.OSDescription}",
                Severity = FindingSeverity.Warning,
                Evidence = $"Runtime reported non-Windows OS: {RuntimeInformation.OSDescription}.",
                ActionState = FindingActionState.Unavailable,
                AdminRequirement = FindingAdminRequirement.NotRequired,
                Risk = FindingRisk.Unknown,
                VerificationStatus = FindingVerificationStatus.NotVerified
            });
            return Task.FromResult<IReadOnlyList<Finding>>(findings);
        }

        var version = Environment.OSVersion.Version;
        var build = version.Build;
        var displayVersion = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
        var productName = ReadRegistryString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName") ?? "Windows";

        findings.Add(new Finding
        {
            ModuleName = Name,
            Title = $"{productName} (build {build})",
            Details = $"Version {version.Major}.{version.Minor}.{version.Build}" +
                      (string.IsNullOrWhiteSpace(displayVersion) ? string.Empty : $", release {displayVersion}"),
            Severity = FindingSeverity.Info,
            Evidence = string.IsNullOrWhiteSpace(displayVersion)
                ? $"Environment reported Windows version {version} and build {build}; DisplayVersion was unavailable."
                : $"Environment reported Windows version {version}, build {build}, release {displayVersion}.",
            ActionState = FindingActionState.None,
            AdminRequirement = FindingAdminRequirement.NotRequired,
            Risk = FindingRisk.NotApplicable,
            VerificationStatus = FindingVerificationStatus.NotRequired
        });

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static string? ReadRegistryString(string subKey, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }
}
