using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatchGuard.Services.Ml;

/// <summary>
/// Isolation Forest (Liu, Ting, Zhou 2008) — pure C# implementation for offline train
/// and inference. Microsoft.ML 5.0 GA does not ship Isolation Forest; this artifact is
/// trained offline and bundled with the app (no user-facing training UI).
/// </summary>
public sealed class IsolationForestModel
{
    public const string ArtifactFileName = "isolation-forest-v1.json";

    public int Trees { get; init; }
    public int SampleSize { get; init; }
    public int Seed { get; init; }
    public double Contamination { get; init; }
    public double ScoreThreshold { get; init; }
    public IReadOnlyList<IsolationTreeDto> Forest { get; init; } = [];

    public static IsolationForestModel Train(
        IReadOnlyList<float[]> features,
        int trees = 100,
        int sampleSize = 64,
        double contamination = 0.05,
        int seed = 42)
    {
        if (features.Count == 0)
        {
            throw new ArgumentException("Training set is empty.", nameof(features));
        }

        var dimensions = features[0].Length;
        if (features.Any(f => f.Length != dimensions))
        {
            throw new ArgumentException("All feature vectors must share the same length.");
        }

        sampleSize = Math.Clamp(sampleSize, 2, features.Count);
        var rng = new Random(seed);
        var forest = new List<IsolationTreeDto>(trees);
        var maxDepth = (int)Math.Ceiling(Math.Log2(sampleSize));

        for (var t = 0; t < trees; t++)
        {
            var sample = new float[sampleSize][];
            for (var i = 0; i < sampleSize; i++)
            {
                sample[i] = features[rng.Next(features.Count)];
            }

            forest.Add(BuildTree(sample, 0, maxDepth, rng));
        }

        var model = new IsolationForestModel
        {
            Trees = trees,
            SampleSize = sampleSize,
            Seed = seed,
            Contamination = contamination,
            ScoreThreshold = 0,
            Forest = forest
        };

        // Threshold = contamination quantile of training anomaly scores (higher = more anomalous).
        var scores = features.Select(f => model.AnomalyScore(f)).OrderByDescending(s => s).ToArray();
        var index = Math.Clamp((int)(contamination * scores.Length), 0, scores.Length - 1);
        return new IsolationForestModel
        {
            Trees = trees,
            SampleSize = sampleSize,
            Seed = seed,
            Contamination = contamination,
            ScoreThreshold = scores[index],
            Forest = forest
        };
    }

    public double AnomalyScore(float[] features) => AnomalyScore((ReadOnlySpan<float>)features);

    public double AnomalyScore(ReadOnlySpan<float> features)
    {
        if (Forest.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (var tree in Forest)
        {
            sum += PathLength(tree, features, 0);
        }

        var avgPath = sum / Forest.Count;
        var c = AveragePathLength(SampleSize);
        // Classic IF score in (0,1]; higher ⇒ more anomalous.
        return Math.Pow(2, -avgPath / c);
    }

    public bool IsAnomaly(float[] features, out double score)
    {
        score = AnomalyScore(features);
        return score >= ScoreThreshold;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    public static IsolationForestModel Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<IsolationForestModel>(json, SerializerOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize Isolation Forest from {path}.");
    }

    public static IsolationForestModel Load(Stream stream)
    {
        return JsonSerializer.Deserialize<IsolationForestModel>(stream, SerializerOptions)
               ?? throw new InvalidOperationException("Failed to deserialize Isolation Forest stream.");
    }

    private static IsolationTreeDto BuildTree(IReadOnlyList<float[]> sample, int depth, int maxDepth, Random rng)
    {
        if (sample.Count <= 1 || depth >= maxDepth)
        {
            return IsolationTreeDto.Leaf(sample.Count);
        }

        var dim = sample[0].Length;
        var featureIndex = rng.Next(dim);
        var min = sample.Min(r => r[featureIndex]);
        var max = sample.Max(r => r[featureIndex]);
        if (Math.Abs(max - min) < 1e-12f)
        {
            return IsolationTreeDto.Leaf(sample.Count);
        }

        var split = min + (float)rng.NextDouble() * (max - min);
        var left = sample.Where(r => r[featureIndex] < split).ToArray();
        var right = sample.Where(r => r[featureIndex] >= split).ToArray();
        if (left.Length == 0 || right.Length == 0)
        {
            return IsolationTreeDto.Leaf(sample.Count);
        }

        return new IsolationTreeDto
        {
            FeatureIndex = featureIndex,
            SplitValue = split,
            Size = sample.Count,
            Left = BuildTree(left, depth + 1, maxDepth, rng),
            Right = BuildTree(right, depth + 1, maxDepth, rng)
        };
    }

    private static double PathLength(IsolationTreeDto node, ReadOnlySpan<float> features, int depth)
    {
        if (node.Left is null || node.Right is null)
        {
            return depth + AveragePathLength(node.Size);
        }

        return features[node.FeatureIndex] < node.SplitValue
            ? PathLength(node.Left, features, depth + 1)
            : PathLength(node.Right, features, depth + 1);
    }

    /// <summary>Average path length of unsuccessful BST search (c(n) in the IF paper).</summary>
    internal static double AveragePathLength(int n)
    {
        if (n <= 1)
        {
            return 0;
        }

        if (n == 2)
        {
            return 1;
        }

        return 2.0 * (Harmonic(n - 1) - 1.0);
    }

    private static double Harmonic(int n) =>
        // H(n) ≈ ln(n) + γ
        Math.Log(n) + 0.5772156649;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class IsolationTreeDto
{
    public int FeatureIndex { get; init; } = -1;
    public float SplitValue { get; init; }
    public int Size { get; init; }
    public IsolationTreeDto? Left { get; init; }
    public IsolationTreeDto? Right { get; init; }

    public static IsolationTreeDto Leaf(int size) => new() { Size = size };
}
