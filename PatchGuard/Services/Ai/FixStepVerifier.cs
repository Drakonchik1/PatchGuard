using System.Text.Json;
using System.Text.RegularExpressions;
using PatchGuard.Models;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Rejects privileged / destructive council fix steps before they reach the Guide UI.
/// </summary>
public static partial class FixStepVerifier
{
    private static readonly string[] ForbiddenPhrases =
    [
        "dism ",
        "dism.exe",
        "sfc /scannow",
        "sfc.exe",
        "regedit",
        "reg add ",
        "reg delete ",
        "format c:",
        "diskpart",
        "takeown",
        "icacls",
        "bcdedit",
        "powershell -enc",
        "powershell -encodedcommand",
        "net user ",
        "net localgroup administrators",
        "schtasks /create",
        "rd /s",
        "del /f /s",
        "remove-item -recurse",
        "start-service ",
        "sc start ",
        "net start ",
        "stop-service ",
        "sc stop ",
        "net stop "
    ];

    public static StepVerificationResult VerifyChiefJson(string chiefRaw)
    {
        try
        {
            var json = ExtractJson(chiefRaw);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("steps", out var stepsElement) ||
                stepsElement.ValueKind != JsonValueKind.Array)
            {
                return StepVerificationResult.Valid();
            }

            var reasons = new List<string>();
            var index = 0;
            foreach (var step in stepsElement.EnumerateArray())
            {
                index++;
                var title = GetString(step, "title") ?? $"Step {index}";
                var instructions = GetString(step, "instructions") ?? string.Empty;
                var linkUrl = GetString(step, "linkUrl");

                foreach (var reason in DescribeUnsafe(title, instructions, linkUrl))
                {
                    reasons.Add($"Step {index} ({title}): {reason}");
                }
            }

            return reasons.Count == 0
                ? StepVerificationResult.Valid()
                : StepVerificationResult.Rejected(reasons);
        }
        catch (JsonException)
        {
            // Unparseable JSON is handled by the council fallback path — not a verify failure.
            return StepVerificationResult.Valid();
        }
    }

    public static IEnumerable<string> DescribeUnsafe(string title, string instructions, string? linkUrl)
    {
        var haystack = $"{title}\n{instructions}";
        foreach (var phrase in ForbiddenPhrases)
        {
            if (haystack.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                yield return $"forbidden privileged action ({phrase.Trim()})";
            }
        }

        if (ShellCommandPattern().IsMatch(haystack))
        {
            yield return "shell command pattern that requires elevation";
        }

        if (!string.IsNullOrWhiteSpace(linkUrl) &&
            !LaunchUriPolicy.TryNormalize(linkUrl, out _))
        {
            yield return $"unsafe linkUrl '{linkUrl}'";
        }
    }

    public static bool IsSafe(FixStep step) =>
        !DescribeUnsafe(step.Title, step.Instructions, step.LinkUrl).Any();

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    [GeneratedRegex(
        @"\b(cmd\.exe|powershell\.exe|pwsh\.exe)\b.*\b(-Command|/c)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShellCommandPattern();
}

public sealed class StepVerificationResult
{
    public required bool IsValid { get; init; }
    public IReadOnlyList<string> RejectionReasons { get; init; } = [];

    public static StepVerificationResult Valid() => new() { IsValid = true };

    public static StepVerificationResult Rejected(IReadOnlyList<string> reasons) =>
        new() { IsValid = false, RejectionReasons = reasons };
}
