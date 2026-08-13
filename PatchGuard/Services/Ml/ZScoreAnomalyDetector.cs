using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Ml;

/// <summary>Pure C# z-score baseline detector for univariate sensor series.</summary>
public sealed class ZScoreAnomalyDetector : IAnomalyDetector
{
    public const int DefaultMinSamples = 20;
    public const double DefaultZThreshold = 3.0;

    private readonly int _minSamples;
    private readonly double _zThreshold;

    public ZScoreAnomalyDetector(int minSamples = DefaultMinSamples, double zThreshold = DefaultZThreshold)
    {
        if (minSamples < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(minSamples), "Need at least 3 samples.");
        }

        if (zThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zThreshold));
        }

        _minSamples = minSamples;
        _zThreshold = zThreshold;
    }

    public string Name => "Z-score";
    public bool IsAvailable => true;

    public IReadOnlyList<AnomalyHit> Detect(IReadOnlyList<SensorSnapshotRecord> history)
    {
        if (history.Count < _minSamples)
        {
            return [];
        }

        var hits = new List<AnomalyHit>();
        foreach (var (key, displayName, selector) in SensorMetricSeries.Metrics)
        {
            var series = SensorMetricSeries.Extract(history, selector);
            if (series.Count < _minSamples)
            {
                continue;
            }

            if (TryScoreLatest(series, out var hit))
            {
                hits.Add(new AnomalyHit
                {
                    SensorName = displayName,
                    MetricKey = key,
                    Value = hit.Value,
                    Mean = hit.Mean,
                    StdDev = hit.StdDev,
                    ZScore = hit.ZScore,
                    ConfidencePercent = ConfidenceFromZ(hit.ZScore),
                    Explanation = FormatExplanation(displayName, key, hit.Value, hit.Mean, hit.StdDev, hit.ZScore),
                    Severity = SeverityFromZ(hit.ZScore),
                    DetectorName = Name
                });
            }
        }

        return hits;
    }

    /// <summary>Scores one synthetic series (for unit tests / metrics).</summary>
    public bool TryScoreSeries(
        IReadOnlyList<double> series,
        out double value,
        out double mean,
        out double stdDev,
        out double zScore)
    {
        value = mean = stdDev = zScore = 0;
        if (series.Count < _minSamples)
        {
            return false;
        }

        if (!TryScoreLatest(series, out var hit))
        {
            return false;
        }

        value = hit.Value;
        mean = hit.Mean;
        stdDev = hit.StdDev;
        zScore = hit.ZScore;
        return true;
    }

    private bool TryScoreLatest(IReadOnlyList<double> series, out (double Value, double Mean, double StdDev, double ZScore) hit)
    {
        hit = default;
        // Baseline excludes the latest point so a spike does not inflate μ/σ.
        var baseline = series.Take(series.Count - 1).ToArray();
        if (baseline.Length < _minSamples - 1)
        {
            return false;
        }

        var mean = baseline.Average();
        var variance = baseline.Sum(v => (v - mean) * (v - mean)) / baseline.Length;
        var stdDev = Math.Sqrt(variance);
        if (stdDev < 1e-9)
        {
            // Flat baseline: only flag if the latest point moved away from the plateau.
            var latestFlat = series[^1];
            if (Math.Abs(latestFlat - mean) < 1e-6)
            {
                return false;
            }

            stdDev = Math.Max(Math.Abs(latestFlat - mean) / _zThreshold, 1e-3);
        }

        var latest = series[^1];
        var z = (latest - mean) / stdDev;
        if (Math.Abs(z) < _zThreshold)
        {
            return false;
        }

        hit = (latest, mean, stdDev, z);
        return true;
    }

    internal static double ConfidenceFromZ(double zScore)
    {
        var abs = Math.Abs(zScore);
        // Map |z|≈3 → ~70%, |z|≈5 → ~90%, |z|≥8 → ~99%.
        var confidence = 100.0 * (1.0 - Math.Exp(-0.35 * (abs - 2.0)));
        return Math.Clamp(confidence, 50, 99.5);
    }

    internal static FindingSeverity SeverityFromZ(double zScore)
    {
        var abs = Math.Abs(zScore);
        if (abs >= 5.0)
        {
            return FindingSeverity.Critical;
        }

        if (abs >= 3.5)
        {
            return FindingSeverity.Warning;
        }

        return FindingSeverity.Info;
    }

    internal static string FormatExplanation(
        string displayName,
        string metricKey,
        double value,
        double mean,
        double stdDev,
        double zScore) =>
        $"{displayName} {SensorMetricSeries.FormatValue(metricKey, value)} vs baseline μ={mean:F1} σ={stdDev:F1} (z={zScore:F1})";
}
