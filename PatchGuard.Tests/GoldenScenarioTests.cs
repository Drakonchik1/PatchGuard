using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PatchGuard.Models;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class GoldenScenarioTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CouncilEvaluator _evaluator = new();

    [Fact]
    public void TenGoldenScenariosMatchExpectedBaselineScores()
    {
        var scenarios = LoadScenarios();

        Assert.Equal(10, scenarios.Count);

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

        Assert.Equal(91.7, averageActionability);
        Assert.Equal(95.0, averageConsistency);
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
