using System.IO;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace PatchGuard.Services.Ml;

/// <summary>
/// Offline-only trainer. Invoked from tests / a one-shot helper — never from product UI.
/// </summary>
public static class MlOfflineTrainer
{
    public const int FeatureDimensions = 5;

    /// <summary>
    /// Trains Isolation Forest + Microsoft.ML RandomizedPCA on synthetic healthy sensor data
    /// and writes artifacts to <paramref name="outputDirectory"/>.
    /// </summary>
    public static void TrainAndSaveArtifacts(string outputDirectory, int seed = 42)
    {
        Directory.CreateDirectory(outputDirectory);
        var normal = GenerateNormalSamples(count: 400, seed: seed);
        var features = normal.Select(ToVector).ToList();

        var forest = IsolationForestModel.Train(
            features,
            trees: 80,
            sampleSize: 64,
            contamination: 0.05,
            seed: seed);
        forest.Save(Path.Combine(outputDirectory, IsolationForestModel.ArtifactFileName));

        TrainAndSaveRandomizedPca(normal, Path.Combine(outputDirectory, MlNetAnomalyDetector.RandomizedPcaFileName), seed);
    }

    public static void TrainAndSaveRandomizedPca(
        IReadOnlyList<MlNetAnomalyDetector.SensorFeatureRow> normalRows,
        string zipPath,
        int seed = 42)
    {
        var ml = new MLContext(seed: seed);
        var data = ml.Data.LoadFromEnumerable(normalRows);
        var pipeline = ml.Transforms
            .Concatenate(
                "Features",
                nameof(MlNetAnomalyDetector.SensorFeatureRow.CpuTemperatureC),
                nameof(MlNetAnomalyDetector.SensorFeatureRow.CpuLoadPercent),
                nameof(MlNetAnomalyDetector.SensorFeatureRow.GpuTemperatureC),
                nameof(MlNetAnomalyDetector.SensorFeatureRow.GpuLoadPercent),
                nameof(MlNetAnomalyDetector.SensorFeatureRow.RamLoadPercent))
            .Append(ml.AnomalyDetection.Trainers.RandomizedPca(
                featureColumnName: "Features",
                rank: 2,
                ensureZeroMean: true,
                seed: seed));

        var model = pipeline.Fit(data);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        ml.Model.Save(model, data.Schema, zipPath);
    }

    public static List<MlNetAnomalyDetector.SensorFeatureRow> GenerateNormalSamples(int count, int seed)
    {
        var rng = new Random(seed);
        var rows = new List<MlNetAnomalyDetector.SensorFeatureRow>(count);
        for (var i = 0; i < count; i++)
        {
            rows.Add(new MlNetAnomalyDetector.SensorFeatureRow
            {
                CpuTemperatureC = (float)SampleNormal(rng, 55, 4),
                CpuLoadPercent = (float)Math.Clamp(SampleNormal(rng, 25, 8), 1, 70),
                GpuTemperatureC = (float)SampleNormal(rng, 50, 5),
                GpuLoadPercent = (float)Math.Clamp(SampleNormal(rng, 20, 10), 0, 80),
                RamLoadPercent = (float)Math.Clamp(SampleNormal(rng, 45, 6), 10, 85)
            });
        }

        return rows;
    }

    /// <summary>
    /// Fixed synthetic evaluation set: mostly normal rows plus labeled spikes.
    /// Used by precision/recall/F1 tests.
    /// </summary>
    public static (List<LabeledSample> Samples, IsolationForestModel Forest) CreateEvaluationSet(int seed = 7)
    {
        var normal = GenerateNormalSamples(200, seed);
        var forest = IsolationForestModel.Train(
            normal.Select(ToVector).ToList(),
            trees: 80,
            sampleSize: 64,
            contamination: 0.05,
            seed: seed);

        var rng = new Random(seed + 99);
        var samples = new List<LabeledSample>();
        foreach (var row in normal.Take(80))
        {
            samples.Add(new LabeledSample(ToVector(row), IsAnomaly: false));
        }

        // Clear temperature / load spikes that should be recalled as anomalies.
        for (var i = 0; i < 20; i++)
        {
            samples.Add(new LabeledSample(
                [
                    (float)(95 + rng.NextDouble() * 10),
                    (float)(90 + rng.NextDouble() * 8),
                    (float)(92 + rng.NextDouble() * 8),
                    (float)(88 + rng.NextDouble() * 10),
                    (float)(80 + rng.NextDouble() * 10)
                ],
                IsAnomaly: true));
        }

        return (samples, forest);
    }

    public static float[] ToVector(MlNetAnomalyDetector.SensorFeatureRow row) =>
    [
        row.CpuTemperatureC,
        row.CpuLoadPercent,
        row.GpuTemperatureC,
        row.GpuLoadPercent,
        row.RamLoadPercent
    ];

    public static double SampleNormal(Random rng, double mean, double stdDev)
    {
        // Box-Muller
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * randStdNormal;
    }

    public readonly record struct LabeledSample(float[] Features, bool IsAnomaly);

    public static (double Precision, double Recall, double F1) ScoreBinary(
        IReadOnlyList<bool> expected,
        IReadOnlyList<bool> predicted)
    {
        if (expected.Count != predicted.Count || expected.Count == 0)
        {
            throw new ArgumentException("Expected and predicted must be non-empty and equal length.");
        }

        var tp = 0;
        var fp = 0;
        var fn = 0;
        for (var i = 0; i < expected.Count; i++)
        {
            if (predicted[i] && expected[i])
            {
                tp++;
            }
            else if (predicted[i] && !expected[i])
            {
                fp++;
            }
            else if (!predicted[i] && expected[i])
            {
                fn++;
            }
        }

        var precision = tp + fp == 0 ? 0 : (double)tp / (tp + fp);
        var recall = tp + fn == 0 ? 0 : (double)tp / (tp + fn);
        var f1 = precision + recall < 1e-12 ? 0 : 2 * precision * recall / (precision + recall);
        return (precision, recall, f1);
    }
}
