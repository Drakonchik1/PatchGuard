using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatchGuard.Models;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class GoldenScenarioTests
{
    /// <summary>Sprint 5 locked baseline averages (15 fixtures).</summary>
    public const double BaselineActionability = 94.4;
    public const double BaselineConsistency = 96.7;
    public const double MaxRegressionRatio = 0.05;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CouncilEvaluator _evaluator = new();

    [Fact]
    public void FifteenGoldenScenariosMatchExpectedBaselineScores()
    {
        var scenarios = LoadScenarios();

        Assert.True(scenarios.Count >= 15, $"Expected ≥15 golden fixtures, found {scenarios.Count}.");

        foreach (var scenario in scenarios)
        {
            var metrics = _evaluator.Evaluate(scenario.Guide);
            Assert.Equal(scenario.ExpectedActionabilityScore, metrics.ActionabilityScore);
            Assert.Equal(scenario.ExpectedConsistencyScore, metrics.ConsistencyScore);
        }
    }

    [Fact]
    public void GoldenScenarioAveragesDefineCurrentBaseline()
    {
        var scenarios = LoadScenarios();

        var averageActionability = Math.Round(scenarios.Average(s => s.ExpectedActionabilityScore), 1);
        var averageConsistency = Math.Round(scenarios.Average(s => s.ExpectedConsistencyScore), 1);

        Assert.Equal(BaselineActionability, averageActionability);
        Assert.Equal(BaselineConsistency, averageConsistency);
    }

    [Fact]
    public void GoldenAveragesMustNotDropMoreThanFivePercentVsBaseline()
    {
        var scenarios = LoadScenarios();
        var averageActionability = scenarios.Average(s =>
        {
            var metrics = _evaluator.Evaluate(s.Guide);
            return metrics.ActionabilityScore;
        });
        var averageConsistency = scenarios.Average(s =>
        {
            var metrics = _evaluator.Evaluate(s.Guide);
            return metrics.ConsistencyScore;
        });

        var minActionability = BaselineActionability * (1 - MaxRegressionRatio);
        var minConsistency = BaselineConsistency * (1 - MaxRegressionRatio);

        Assert.True(
            averageActionability >= minActionability,
            $"Actionability {averageActionability:F1} dropped more than 5% below baseline {BaselineActionability}.");
        Assert.True(
            averageConsistency >= minConsistency,
            $"Consistency {averageConsistency:F1} dropped more than 5% below baseline {BaselineConsistency}.");
    }

    private static IReadOnlyList<GoldenScenarioFixture> LoadScenarios()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GoldenScenarios");
        return Directory.GetFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => JsonSerializer.Deserialize<GoldenScenarioFixture>(File.ReadAllText(path), JsonOptions))
            .Cast<GoldenScenarioFixture>()
            .ToList();
    }

    public sealed class GoldenScenarioFixture
    {
        public required string Name { get; init; }
        public required ScanScenario Scenario { get; init; }
        public required double ExpectedActionabilityScore { get; init; }
        public required double ExpectedConsistencyScore { get; init; }
        public required RepairGuide Guide { get; init; }
    }
}
