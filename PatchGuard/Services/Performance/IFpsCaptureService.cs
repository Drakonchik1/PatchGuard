using PatchGuard.Models;

namespace PatchGuard.Services.Performance;

public interface IFpsCaptureService
{
    /// <summary>True when a verified, signed PresentMon executable was found.</summary>
    bool IsAvailable { get; }

    /// <summary>When not available, an optional human-readable reason (e.g. unsigned binary).</summary>
    string? UnavailableReason { get; }

    /// <summary>Lists running processes that own a visible top-level window.</summary>
    IReadOnlyList<GameProcessInfo> GetCandidateProcesses();

    /// <summary>
    /// Runs a timed PresentMon capture against the target process and computes
    /// Average / 1% low / 0.1% low FPS. Never throws; failures are reported via
    /// <see cref="FpsCaptureResult.Success"/> and Message.
    /// </summary>
    Task<FpsCaptureResult> CaptureAsync(
        GameProcessInfo target,
        int seconds,
        CancellationToken cancellationToken = default);
}
