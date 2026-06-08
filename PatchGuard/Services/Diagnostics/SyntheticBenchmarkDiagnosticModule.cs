using Microsoft.EntityFrameworkCore;
using PatchGuard.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Performance;

namespace PatchGuard.Services.Diagnostics;

public sealed class SyntheticBenchmarkDiagnosticModule : IDiagnosticModule
{
    private readonly ISyntheticBenchmarkRunner _benchmark;
    private readonly PatchGuardDbContext _db;

    public SyntheticBenchmarkDiagnosticModule(ISyntheticBenchmarkRunner benchmark, PatchGuardDbContext db)
    {
        _benchmark = benchmark;
        _db = db;
    }

    public string Name => "Render test";
    public string Description => "2s on-screen test — compare trend, not game FPS.";
    public bool IsImplemented => true;

    public async Task<IReadOnlyList<Finding>> RunAsync(CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        var fps = await _benchmark.RunAsync(cancellationToken);

        var last = await _db.BenchmarkRecords
            .OrderByDescending(r => r.RecordedAt)
            .FirstOrDefaultAsync(cancellationToken);

        string details;
        FindingSeverity severity;

        if (last is null)
        {
            details = $"Score {fps:F0} FPS (synthetic). Run again after PC changes to compare.";
            severity = FindingSeverity.Info;
        }
        else
        {
            var delta = fps - last.SyntheticFps;
            var pct = last.SyntheticFps > 0 ? delta / last.SyntheticFps * 100 : 0;
            details = $"Now {fps:F0} vs last {last.SyntheticFps:F0} ({pct:+0;-0}%). Same settings each run.";
            severity = pct < -15 ? FindingSeverity.Warning : FindingSeverity.Info;
        }

        _db.BenchmarkRecords.Add(new BenchmarkRecord
        {
            RecordedAt = DateTime.UtcNow,
            SyntheticFps = fps,
            GpuName = string.Empty
        });
        await _db.SaveChangesAsync(cancellationToken);

        findings.Add(new Finding
        {
            ModuleName = Name,
            Title = $"Synthetic render: {fps:F0} FPS",
            Details = details,
            Severity = severity
        });

        return findings;
    }
}
