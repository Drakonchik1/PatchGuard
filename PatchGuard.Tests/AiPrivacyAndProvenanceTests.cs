using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PatchGuard.Models;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Health;

namespace PatchGuard.Tests;

public sealed class AiPrivacyAndProvenanceTests
{
    [Fact]
    public async Task ExternalServicesAreNotCalledWithoutAffirmativeConsent()
    {
        var handler = new CapturingOpenAiHandler();
        var search = new CapturingWebSearch([]);
        var service = CreateService(handler, search);

        var guide = await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [SensitiveFinding()],
            allowExternalServices: false);

        Assert.Empty(search.Queries);
        Assert.Empty(handler.Payloads);
        Assert.Equal([GuidanceSource.Local], guide.Sources);
    }

    [Fact]
    public async Task ExternalPayloadOmitsPersonalPathsHostnamesAndSecrets()
    {
        var handler = new CapturingOpenAiHandler();
        var search = new CapturingWebSearch([]);
        var service = CreateService(handler, search);

        await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [SensitiveFinding()],
            allowExternalServices: true);

        var transmitted = string.Join("\n", search.Queries.Concat(handler.Payloads));
        Assert.DoesNotContain("alice", transmitted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DESKTOP-PRIVATE", transmitted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users", transmitted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-secret-value", transmitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Event payload contains private application text", transmitted, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownDiagnosticCategoryIsGeneralizedInsteadOfPartiallyMasked()
    {
        var finding = SensitiveFinding(
            @"User alice token=sk-secret-value C:\private");

        var transmitted = string.Join(
            "\n",
            ExternalDiagnosticSanitizer.BuildSearchQueries([finding]));

        Assert.Equal("Windows 11 Windows diagnostic troubleshooting", transmitted);
    }

    [Fact]
    public async Task ExternalServicesAreNotCalledWhenProvidersAreNotConfigured()
    {
        var handler = new CapturingOpenAiHandler();
        var search = new CapturingWebSearch([]) { IsConfiguredOverride = false };
        var service = new AiCouncilService(
            new OpenAiChatClient(new HttpClient(handler), new AiOptions()),
            search,
            new HealthScorePolicy());

        await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [SensitiveFinding()],
            allowExternalServices: true);

        Assert.Empty(search.Queries);
        Assert.Empty(handler.Payloads);
    }

    [Fact]
    public async Task ReferencesRetainRecommendationAssociationAndRejectUnsafeUrls()
    {
        var results = new[]
        {
            new WebSearchResult
            {
                Title = "Vendor guidance",
                Url = "https://support.example.com/windows/fix",
                Snippet = "Supported repair steps"
            },
            new WebSearchResult
            {
                Title = "Unsafe",
                Url = "file:///C:/secret.txt",
                Snippet = "Do not use"
            }
        };
        var service = CreateService(
            new CapturingOpenAiHandler(),
            new CapturingWebSearch(results));

        var guide = await service.BuildGuideAsync(
            ScanScenario.QuickHealthCheck,
            [SensitiveFinding()],
            allowExternalServices: true);

        var reference = Assert.Single(guide.WebReferences);
        Assert.Equal("Vendor guidance", reference.Title);
        Assert.Equal("support.example.com", reference.Domain);
        Assert.Contains("Event logs", reference.UsedFor, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(GuidanceSource.WebSourced, guide.Sources);
    }

    [Theory]
    [InlineData("https://example.com/path", true)]
    [InlineData("http://example.com/path", true)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:password@example.com", false)]
    [InlineData("not a url", false)]
    public void ExternalUrlPolicyAllowsOnlySafeWebLinks(string url, bool expected) =>
        Assert.Equal(expected, ExternalUrlPolicy.TryNormalize(url, out _));

    private static AiCouncilService CreateService(
        CapturingOpenAiHandler handler,
        CapturingWebSearch search)
    {
        var options = new AiOptions { ApiKey = "configured", Model = "test-model" };
        return new AiCouncilService(
            new OpenAiChatClient(new HttpClient(handler), options),
            search,
            new HealthScorePolicy());
    }

    private static Finding SensitiveFinding(string moduleName = "Event logs") =>
        new()
        {
            ModuleName = moduleName,
            Title = @"Failure for alice on DESKTOP-PRIVATE at C:\Users\alice\AppData\Local\app.exe",
            Details = "Event payload contains private application text; token=sk-secret-value",
            Severity = FindingSeverity.Warning,
            Risk = FindingRisk.Medium
        };

    private sealed class CapturingWebSearch(
        IReadOnlyList<WebSearchResult> results) : IWebSearchService
    {
        public bool IsConfiguredOverride { get; set; } = true;

        public bool IsConfigured => IsConfiguredOverride;

        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(results);
        }
    }

    private sealed class CapturingOpenAiHandler : HttpMessageHandler
    {
        public List<string> Payloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            const string chiefResponse =
                """{"summary":"Ready","verdict":"Use measured findings.","steps":[]}""";
            var payload = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = chiefResponse } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
