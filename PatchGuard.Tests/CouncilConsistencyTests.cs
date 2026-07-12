using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

public sealed class CouncilConsistencyTests
{
    [Fact]
    public async Task AiGuideUsesSharedHealthScoreAndIncludesLocalSource()
    {
        var findings = new[] { CreateFinding(FindingSeverity.Warning) };
        var policy = new HealthScorePolicy();
        var service = CreateCouncilService([], policy);

        var guide = await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            findings,
            allowExternalServices: true);

        Assert.Equal(policy.Calculate(findings), guide.HealthScore);
        Assert.Equal([GuidanceSource.Local, GuidanceSource.AiGenerated], guide.Sources);
        Assert.DoesNotContain(GuidanceSource.WebSourced, guide.Sources);
    }

    [Fact]
    public async Task AiGuideIncludesWebSourceOnlyWhenResultsWereUsed()
    {
        var policy = new HealthScorePolicy();
        var service = CreateCouncilService(
        [
            new WebSearchResult
            {
                Title = "Vendor guidance",
                Url = "https://example.com/fix",
                Snippet = "Verified troubleshooting steps"
            }
        ],
        policy);

        var guide = await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [CreateFinding(FindingSeverity.Warning)],
            allowExternalServices: true);

        Assert.Equal(
            [GuidanceSource.Local, GuidanceSource.AiGenerated, GuidanceSource.WebSourced],
            guide.Sources);
    }

    [Fact]
    public void CouncilImplementationsDependOnSharedPolicyWithoutPrivateCalculators()
    {
        Assert.Contains(
            typeof(IHealthScorePolicy),
            typeof(AiCouncilService).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Contains(
            typeof(IHealthScorePolicy),
            typeof(LocalCouncilSession).GetConstructors().Single().GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(
            typeof(AiCouncilService).GetMethods(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
            method => method.Name.Contains("ComputeHealthScore", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(LocalCouncilSession).GetMethods(
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static),
            method => method.Name.Contains("ComputeHealthScore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LocalCouncilDoesNotTurnUnavailableWarningIntoCorrectiveStep()
    {
        var finding = CreateFinding(FindingSeverity.Warning);
        var policy = new HealthScorePolicy();
        var council = new LocalCouncilSession(policy);

        var guide = await council.RunAsync(
            ScanScenario.QuickHealthCheck,
            [finding],
            [],
            [],
            new CouncilProgressReporter(null),
            CancellationToken.None);

        Assert.DoesNotContain(guide.Steps, step => step.Title == finding.Title);
        Assert.Equal(policy.Calculate([finding]), guide.HealthScore);
    }

    private static AiCouncilService CreateCouncilService(
        IReadOnlyList<WebSearchResult> webResults,
        IHealthScorePolicy policy)
    {
        var options = new AiOptions { ApiKey = "test-key", Model = "test-model" };
        var openAi = new OpenAiChatClient(
            new HttpClient(new StubOpenAiHandler()),
            options);
        return new AiCouncilService(openAi, new StubWebSearch(webResults), policy);
    }

    private static Finding CreateFinding(FindingSeverity severity) =>
        new()
        {
            ModuleName = "CPU load",
            Title = "CPU load test",
            Details = "Measured CPU load.",
            Severity = severity
        };

    private sealed class StubWebSearch(IReadOnlyList<WebSearchResult> results) : IWebSearchService
    {
        public bool IsConfigured => results.Count > 0;

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(results);
    }

    private sealed class StubOpenAiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            const string chiefResponse =
                """{"summary":"Ready","verdict":"Use measured findings.","healthScore":1,"steps":[]}""";
            var payload = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = chiefResponse } }
                }
            });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
