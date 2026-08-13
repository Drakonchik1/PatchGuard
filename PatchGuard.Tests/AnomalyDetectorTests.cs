using System.IO;
using PatchGuard.Data.Entities;
using PatchGuard.Models;
using PatchGuard.Services.Diagnostics;
using PatchGuard.Services.History;
using PatchGuard.Services.Ml;

namespace PatchGuard.Tests;

public sealed class AnomalyDetectorTests
{
    /// <summary>Agreed metric floors — documented in docs/ML_REPORT.md.</summary>
    public const double MinPrecision = 0.80;
    public const double MinRecall = 0.80;
    public const double MinF1 = 0.80;

    [Fact]
    public void ZScore_DetectsSyntheticTemperatureSpike()
    {
        var detector = new ZScoreAnomalyDetector(minSamples: 20, zThreshold: 3.0);
        var series = Enumerable.Repeat(55.0, 40).Append(97.0).ToArray();

        Assert.True(detector.TryScoreSeries(series, out var value, out var mean, out var std, out var z));
        Assert.Equal(97.0, value);
        Assert.InRange(mean, 54.5, 55.5);
        Assert.True(Math.Abs(z) >= 3.0, $"z={z}, std={std}");
    }

    [Fact]
    public void ZScore_NormalBaseline_NoFalsePositive()
    {
        var detector = new ZScoreAnomalyDetector(minSamples: 20, zThreshold: 3.0);
        var rng = new Random(1);
        var series = Enumerable.Range(0, 50)
            .Select(_ => 55.0 + (rng.NextDouble() - 0.5) * 2.0)
            .ToArray();

        Assert.False(detector.TryScoreSeries(series, out _, out _, out _, out _));
    }

    [Fact]
    public void ZScore_HistorySpike_EmitsHitWithExplanation()
    {
        var detector = new ZScoreAnomalyDetector(minSamples: 20);
        var history = BuildHistory(baselineTemp: 55, spikeTemp: 97, count: 40);

        var hits = detector.Detect(history);

        var cpu = Assert.Single(hits, h => h.MetricKey == SensorMetricSeries.CpuTemperatureC);
        Assert.Contains("μ=", cpu.Explanation, StringComparison.Ordinal);
        Assert.Contains("z=", cpu.Explanation, StringComparison.Ordinal);
        Assert.InRange(cpu.ConfidencePercent, 50, 100);
        Assert.True(cpu.Severity >= FindingSeverity.Warning);
    }

    [Fact]
    public void IsolationForest_MetricsMeetFloor_OnSyntheticDataset()
    {
        var (samples, forest) = MlOfflineTrainer.CreateEvaluationSet(seed: 7);
        var expected = samples.Select(s => s.IsAnomaly).ToList();
        var predicted = samples.Select(s => forest.IsAnomaly(s.Features, out _)).ToList();

        var (precision, recall, f1) = MlOfflineTrainer.ScoreBinary(expected, predicted);

        Assert.True(precision >= MinPrecision, $"precision={precision:F3} < {MinPrecision}");
        Assert.True(recall >= MinRecall, $"recall={recall:F3} < {MinRecall}");
        Assert.True(f1 >= MinF1, $"f1={f1:F3} < {MinF1}");
    }

    [Fact]
    public void IsolationForest_NormalSamples_BelowContaminationRate()
    {
        var normal = MlOfflineTrainer.GenerateNormalSamples(100, seed: 11);
        var forest = IsolationForestModel.Train(
            normal.Select(MlOfflineTrainer.ToVector).ToList(),
            trees: 80,
            sampleSize: 64,
            contamination: 0.05,
            seed: 11);

        var falsePositives = normal.Count(row => forest.IsAnomaly(MlOfflineTrainer.ToVector(row), out _));
        Assert.True(falsePositives <= 12, $"falsePositives={falsePositives}");
    }

    [Fact]
    public void OfflineTrainer_WritesBundledArtifacts()
    {
        var output = Path.Combine(Path.GetTempPath(), "PatchGuardMl_" + Guid.NewGuid().ToString("N"));
        try
        {
            MlOfflineTrainer.TrainAndSaveArtifacts(output, seed: 42);
            Assert.True(File.Exists(Path.Combine(output, IsolationForestModel.ArtifactFileName)));
            Assert.True(File.Exists(Path.Combine(output, MlNetAnomalyDetector.RandomizedPcaFileName)));

            var loaded = IsolationForestModel.Load(Path.Combine(output, IsolationForestModel.ArtifactFileName));
            Assert.True(loaded.Forest.Count > 0);
            Assert.True(loaded.ScoreThreshold > 0);
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    [Fact]
    public void MlNetAnomalyDetector_FallsBackToZScore_WhenModelMissing()
    {
        var missingDir = Path.Combine(Path.GetTempPath(), "PatchGuardMlMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(missingDir);
        try
        {
            var detector = new MlNetAnomalyDetector(modelDirectory: missingDir);
            Assert.False(detector.HasBundledModel);
            Assert.Equal("Z-score", detector.Name);

            var history = BuildHistory(baselineTemp: 55, spikeTemp: 98, count: 40);
            var hits = detector.Detect(history);
            Assert.Contains(hits, h => h.MetricKey == SensorMetricSeries.CpuTemperatureC);
        }
        finally
        {
            Directory.Delete(missingDir, recursive: true);
        }
    }

    [Fact]
    public void MlNetAnomalyDetector_WithForest_SurfacesSpike()
    {
        var normal = MlOfflineTrainer.GenerateNormalSamples(200, seed: 3);
        var forest = IsolationForestModel.Train(
            normal.Select(MlOfflineTrainer.ToVector).ToList(),
            trees: 60,
            sampleSize: 48,
            contamination: 0.05,
            seed: 3);

        var detector = new MlNetAnomalyDetector(
            isolationForest: forest,
            randomizedPca: null,
            zScore: new ZScoreAnomalyDetector(minSamples: 20));
        var history = BuildHistory(baselineTemp: 55, spikeTemp: 99, count: 40);
        var hits = detector.Detect(history);

        Assert.NotEmpty(hits);
        Assert.Contains(hits, h => h.Explanation.Contains('μ') || h.MetricKey == "Multivariate");
        Assert.All(hits, h => Assert.InRange(h.ConfidencePercent, 50, 100));
    }

    [Fact]
    public async Task AnomalyDiagnosticModule_EmitsFindingWithConfidence()
    {
        var history = BuildHistory(baselineTemp: 54, spikeTemp: 96, count: 45);
        var module = new AnomalyDiagnosticModule(
            new StubSensorHistory(history),
            new ZScoreAnomalyDetector(minSamples: 20));

        var findings = await module.RunAsync();

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Contains("% confidence", f.Title, StringComparison.OrdinalIgnoreCase));
        var cpu = Assert.Single(findings, f => f.Title.Contains("CPU temperature", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("z=", cpu.Details, StringComparison.Ordinal);
        Assert.True(cpu.Severity >= FindingSeverity.Warning);
    }

    [Fact]
    public async Task AnomalyDiagnosticModule_InsufficientHistory_IsInfo()
    {
        var module = new AnomalyDiagnosticModule(
            new StubSensorHistory([]),
            new ZScoreAnomalyDetector());

        var findings = await module.RunAsync();
        Assert.Contains(findings, f => f.Title.Contains("Not enough", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Regenerates checked-in model artifacts under PatchGuard/Models/Ml.
    /// Opt-in: set env PATCHGUARD_REGEN_ML=1 when intentionally refreshing bundles.
    /// </summary>
    [Fact]
    public void RegenBundledModels_WhenRequested()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PATCHGUARD_REGEN_ML"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var repoModels = FindModelsDirectory();
        MlOfflineTrainer.TrainAndSaveArtifacts(repoModels, seed: 42);
        Assert.True(File.Exists(Path.Combine(repoModels, IsolationForestModel.ArtifactFileName)));
    }

    private static string FindModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "PatchGuard", "Models", "Ml");
            if (Directory.Exists(Path.Combine(dir.FullName, "PatchGuard")))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PatchGuard/Models/Ml.");
    }

    private static List<SensorSnapshotRecord> BuildHistory(double baselineTemp, double spikeTemp, int count)
    {
        var rng = new Random(5);
        var list = new List<SensorSnapshotRecord>(count);
        var start = DateTime.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            var isSpike = i == count - 1;
            list.Add(new SensorSnapshotRecord
            {
                CapturedAt = start.AddMinutes(i),
                CpuTemperatureC = isSpike ? spikeTemp : baselineTemp + (rng.NextDouble() - 0.5),
                CpuLoadPercent = isSpike ? 92 : 20 + rng.NextDouble() * 8,
                GpuTemperatureC = isSpike ? 94 : 48 + (rng.NextDouble() - 0.5) * 2,
                GpuLoadPercent = isSpike ? 90 : 15 + rng.NextDouble() * 10,
                RamLoadPercent = isSpike ? 82 : 40 + rng.NextDouble() * 6
            });
        }

        return list;
    }

    private sealed class StubSensorHistory(IReadOnlyList<SensorSnapshotRecord> records) : ISensorHistoryService
    {
        public Task SaveSnapshotAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SensorSnapshotRecord>> GetRecentAsync(
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SensorSnapshotRecord>>(records.Take(take).ToList());

        public Task<SensorSnapshotRecord?> GetLatestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(records.LastOrDefault());
    }
}
