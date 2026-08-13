using System.IO;
using System.Reflection;
using Microsoft.ML;
using Microsoft.ML.Data;
using PatchGuard.Data.Entities;
using PatchGuard.Models;

namespace PatchGuard.Services.Ml;

/// <summary>
/// Inference-only detector: bundled Isolation Forest (+ optional Microsoft.ML RandomizedPCA).
/// Falls back to <see cref="ZScoreAnomalyDetector"/> when artifacts are missing.
/// </summary>
public sealed class MlNetAnomalyDetector : IAnomalyDetector
{
    public const string IsolationForestFileName = IsolationForestModel.ArtifactFileName;
    public const string RandomizedPcaFileName = "sensor-anomaly-rpca.zip";

    private readonly ZScoreAnomalyDetector _zScore;
    private readonly IsolationForestModel? _isolationForest;
    private readonly ITransformer? _randomizedPca;
    private readonly MLContext? _mlContext;
    private readonly PredictionEngine<SensorFeatureRow, RpcaPrediction>? _rpcaEngine;

    public MlNetAnomalyDetector(
        ZScoreAnomalyDetector? zScore = null,
        string? modelDirectory = null)
    {
        _zScore = zScore ?? new ZScoreAnomalyDetector();
        var directory = modelDirectory ?? ResolveDefaultModelDirectory();

        var forestPath = Path.Combine(directory, IsolationForestFileName);
        if (File.Exists(forestPath))
        {
            try
            {
                _isolationForest = IsolationForestModel.Load(forestPath);
            }
            catch
            {
                _isolationForest = null;
            }
        }

        var rpcaPath = Path.Combine(directory, RandomizedPcaFileName);
        if (File.Exists(rpcaPath))
        {
            try
            {
                _mlContext = new MLContext(seed: 0);
                _randomizedPca = _mlContext.Model.Load(rpcaPath, out _);
                _rpcaEngine = _mlContext.Model.CreatePredictionEngine<SensorFeatureRow, RpcaPrediction>(_randomizedPca);
            }
            catch
            {
                _randomizedPca = null;
                _rpcaEngine = null;
            }
        }
    }

    /// <summary>Test / DI constructor with preloaded models.</summary>
    public MlNetAnomalyDetector(
        IsolationForestModel? isolationForest,
        ITransformer? randomizedPca,
        ZScoreAnomalyDetector? zScore = null,
        MLContext? mlContext = null)
    {
        _zScore = zScore ?? new ZScoreAnomalyDetector();
        _isolationForest = isolationForest;
        _randomizedPca = randomizedPca;
        _mlContext = mlContext ?? (randomizedPca is null ? null : new MLContext(seed: 0));
        if (_randomizedPca is not null && _mlContext is not null)
        {
            _rpcaEngine = _mlContext.Model.CreatePredictionEngine<SensorFeatureRow, RpcaPrediction>(_randomizedPca);
        }
    }

    public string Name =>
        _isolationForest is not null ? "Isolation Forest"
        : _randomizedPca is not null ? "Microsoft.ML RandomizedPCA"
        : _zScore.Name;

    public bool IsAvailable => true;

    public bool HasBundledModel => _isolationForest is not null || _randomizedPca is not null;

    public IReadOnlyList<AnomalyHit> Detect(IReadOnlyList<SensorSnapshotRecord> history)
    {
        if (history.Count < ZScoreAnomalyDetector.DefaultMinSamples)
        {
            return [];
        }

        // Per-sensor z-score explanations remain the user-facing evidence text.
        var zHits = _zScore.Detect(history);

        if (_isolationForest is null && _rpcaEngine is null)
        {
            return zHits;
        }

        var ordered = history.OrderBy(r => r.CapturedAt).ToList();
        var latest = ordered[^1];
        var features = ToFeatureVector(latest);
        if (features is null)
        {
            return zHits;
        }

        var multivariateAnomaly = false;
        double modelScore = 0;

        if (_isolationForest is not null)
        {
            multivariateAnomaly = _isolationForest.IsAnomaly(features, out modelScore);
        }

        if (!multivariateAnomaly && _rpcaEngine is not null)
        {
            var prediction = _rpcaEngine.Predict(ToFeatureRow(features));
            // RandomizedPCA: Score is reconstruction-based; PredictedLabel true ⇒ anomaly.
            multivariateAnomaly = prediction.PredictedLabel;
            modelScore = prediction.Score;
        }

        if (!multivariateAnomaly)
        {
            // Model says normal — suppress z-score false positives on mild deviations.
            return zHits
                .Where(h => Math.Abs(h.ZScore) >= 4.0)
                .Select(h => Annotate(h, modelScore))
                .ToList();
        }

        if (zHits.Count > 0)
        {
            return zHits.Select(h => Annotate(h, modelScore)).ToList();
        }

        // Multivariate flag without a strong univariate z — emit a combined finding.
        return
        [
            new AnomalyHit
            {
                SensorName = "Sensor pattern",
                MetricKey = "Multivariate",
                Value = modelScore,
                Mean = 0,
                StdDev = 0,
                ZScore = 0,
                ConfidencePercent = Math.Clamp(modelScore * 100.0, 55, 99),
                Explanation =
                    $"Multivariate sensor pattern looks anomalous (model score {modelScore:F2}). " +
                    "Compare recent CPU/GPU temp and load against your usual baseline.",
                Severity = FindingSeverity.Warning,
                DetectorName = Name
            }
        ];
    }

    private AnomalyHit Annotate(AnomalyHit hit, double modelScore) =>
        new()
        {
            SensorName = hit.SensorName,
            MetricKey = hit.MetricKey,
            Value = hit.Value,
            Mean = hit.Mean,
            StdDev = hit.StdDev,
            ZScore = hit.ZScore,
            ConfidencePercent = Math.Max(hit.ConfidencePercent, Math.Clamp(modelScore * 100.0, 50, 99)),
            Explanation = $"{hit.Explanation} [{Name} score {modelScore:F2}]",
            Severity = hit.Severity,
            DetectorName = Name
        };

    internal static float[]? ToFeatureVector(SensorSnapshotRecord record)
    {
        // Require core metrics so training/inference feature layout stays stable.
        if (record.CpuTemperatureC is not { } cpuTemp
            || record.CpuLoadPercent is not { } cpuLoad
            || record.GpuTemperatureC is not { } gpuTemp
            || record.GpuLoadPercent is not { } gpuLoad)
        {
            return null;
        }

        return
        [
            (float)cpuTemp,
            (float)cpuLoad,
            (float)gpuTemp,
            (float)gpuLoad,
            (float)(record.RamLoadPercent ?? 40.0)
        ];
    }

    internal static SensorFeatureRow ToFeatureRow(float[] features) =>
        new()
        {
            CpuTemperatureC = features[0],
            CpuLoadPercent = features[1],
            GpuTemperatureC = features[2],
            GpuLoadPercent = features[3],
            RamLoadPercent = features[4]
        };

    private static string ResolveDefaultModelDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Models", "Ml"),
            Path.Combine(baseDir, "Ml"),
            Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? baseDir,
                "Models",
                "Ml")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(baseDir, "Models", "Ml");
    }

    public sealed class SensorFeatureRow
    {
        public float CpuTemperatureC { get; set; }
        public float CpuLoadPercent { get; set; }
        public float GpuTemperatureC { get; set; }
        public float GpuLoadPercent { get; set; }
        public float RamLoadPercent { get; set; }
    }

    private sealed class RpcaPrediction
    {
        [ColumnName("Score")]
        public float Score { get; set; }

        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }
    }
}
