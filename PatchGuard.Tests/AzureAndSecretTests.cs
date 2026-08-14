using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PatchGuard.Services.Ai;
using PatchGuard.Services.Security;

namespace PatchGuard.Tests;

public sealed class AzureOpenAiChatProviderTests
{
    [Fact]
    public async Task CompleteAsyncPostsDeploymentPathWithApiKeyHeader()
    {
        var handler = new RecordingHandler();
        var options = new AiOptions
        {
            AzureEndpoint = "https://example.openai.azure.com/",
            AzureDeployment = "gpt4o-mini",
            AzureApiKey = "azure-secret",
            AzureApiVersion = "2024-06-01"
        };
        var provider = new AzureOpenAiChatProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.openai.azure.com/")
        }, options);

        var reply = await provider.CompleteAsync("system", "user question");

        Assert.Equal("hello from azure", reply);
        Assert.Contains("/openai/deployments/gpt4o-mini/chat/completions", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api-version=2024-06-01", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("azure-secret", handler.ApiKeyHeader);
        Assert.DoesNotContain("\"model\"", handler.Payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsAvailableRequiresEndpointDeploymentAndKey()
    {
        var options = new AiOptions
        {
            AzureEndpoint = "https://example.openai.azure.com/",
            AzureDeployment = "deploy",
            AzureApiKey = "key"
        };
        var provider = new AzureOpenAiChatProvider(new HttpClient(new NoOpHandler()), options);
        Assert.True(provider.IsAvailable);

        options.AzureApiKey = "";
        Assert.False(new AzureOpenAiChatProvider(new HttpClient(new NoOpHandler()), options).IsAvailable);
    }

    [Theory]
    [InlineData("https://resource.openai.azure.com", true)]
    [InlineData("https://resource.services.ai.azure.com/", true)]
    [InlineData("https://openai.azure.com", false)]
    [InlineData("https://services.ai.azure.com", false)]
    [InlineData("http://resource.openai.azure.com", false)]
    [InlineData("https://localhost", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://resource.example.com", false)]
    [InlineData("https://resource.openai.azure.com.evil.example", false)]
    [InlineData("https://evilopenai.azure.com", false)]
    [InlineData("https://-resource.openai.azure.com", false)]
    [InlineData("https://resource-.openai.azure.com", false)]
    [InlineData("https://resource_name.openai.azure.com", false)]
    [InlineData("https://@resource.openai.azure.com", false)]
    [InlineData("https://user@resource.openai.azure.com", false)]
    [InlineData("https://resource.openai.azure.com?", false)]
    [InlineData("https://resource.openai.azure.com?target=evil", false)]
    [InlineData("https://resource.openai.azure.com/#", false)]
    [InlineData("https://resource.openai.azure.com/#fragment", false)]
    public void EndpointPolicyAllowsOnlyOfficialAzureHttpsHosts(string endpoint, bool expected) =>
        Assert.Equal(expected, AzureOpenAiChatProvider.TryNormalizeEndpoint(endpoint, out _));

    [Fact]
    public async Task CompleteAsyncUsesCurrentEndpointAfterSettingsChange()
    {
        var handler = new RecordingHandler();
        var options = new AiOptions
        {
            AzureEndpoint = "https://first.openai.azure.com/",
            AzureDeployment = "gpt4o-mini",
            AzureApiKey = "azure-secret"
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://stale.openai.azure.com/")
        };
        var provider = new AzureOpenAiChatProvider(client, options);

        await provider.CompleteAsync("system", "user question");
        options.AzureEndpoint = "https://second.services.ai.azure.com/";
        await provider.CompleteAsync("system", "follow-up question");

        Assert.Equal(
            new[] { "first.openai.azure.com", "second.services.ai.azure.com" },
            handler.RequestUris.Select(uri => uri.Host));
    }

    [Fact]
    public async Task CompleteAsyncRejectsOversizedUnknownLengthResponse()
    {
        var oversizedPayload = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = new string('x', 1_100_000) } }
            }
        });
        var options = new AiOptions
        {
            AzureEndpoint = "https://example.openai.azure.com/",
            AzureDeployment = "gpt4o-mini",
            AzureApiKey = "azure-secret"
        };
        var provider = new AzureOpenAiChatProvider(
            new HttpClient(new UnknownLengthResponseHandler(oversizedPayload)),
            options);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.CompleteAsync("system", "user"));
    }

    [Fact]
    public async Task BedrockStubIsUnavailableAndThrows()
    {
        var bedrock = new BedrockChatProvider();
        Assert.False(bedrock.IsAvailable);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            bedrock.CompleteAsync("s", "u"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Payload { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;
        public List<Uri> RequestUris { get; } = [];
        public string? ApiKeyHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            RequestUris.Add(request.RequestUri!);
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKeyHeader = request.Headers.TryGetValues("api-key", out var values)
                ? values.FirstOrDefault()
                : null;

            var body = JsonSerializer.Serialize(new
            {
                choices = new[]
                {
                    new { message = new { content = "hello from azure" } }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
    }

    private sealed class UnknownLengthResponseHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(payload)
            });
    }

    private sealed class UnknownLengthContent(string payload) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(payload);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            throw new InvalidOperationException("Response content must not be buffered.");

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

public sealed class SecretStorageTests
{
    [Fact]
    public void HasSecret_ReturnsFalseForCorruptProtectedData()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"patchguard-secrets-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(
                Path.Combine(dir, $"{SecretKeys.AzureOpenAiApiKey}.bin"),
                Encoding.UTF8.GetBytes("not-dpapi-data"));
            var store = new DpapiSecretStorageService(dir);

            Assert.False(store.HasSecret(SecretKeys.AzureOpenAiApiKey));
            Assert.Null(store.GetSecret(SecretKeys.AzureOpenAiApiKey));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void SetSecret_ReplacesThroughSameDirectoryTemporaryFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"patchguard-secrets-{Guid.NewGuid():N}");
        try
        {
            var store = new DpapiSecretStorageService(dir);
            store.SetSecret(SecretKeys.AzureOpenAiApiKey, "first-value");
            using var temporaryFileCreated = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(dir)
            {
                EnableRaisingEvents = true
            };
            watcher.Created += (_, args) =>
            {
                if (!args.Name!.Equals(
                        $"{SecretKeys.AzureOpenAiApiKey}.bin",
                        StringComparison.OrdinalIgnoreCase))
                {
                    temporaryFileCreated.Set();
                }
            };

            store.SetSecret(SecretKeys.AzureOpenAiApiKey, "replacement-value");

            Assert.True(
                temporaryFileCreated.Wait(TimeSpan.FromSeconds(2)),
                "Expected a same-directory temporary file before atomic replacement.");
            Assert.Equal("replacement-value", store.GetSecret(SecretKeys.AzureOpenAiApiKey));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Dpapi_RoundTripsSecretWithoutPlaintextOnDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"patchguard-secrets-{Guid.NewGuid():N}");
        try
        {
            var store = new DpapiSecretStorageService(dir);
            store.SetSecret(SecretKeys.AzureOpenAiApiKey, "super-secret-key");

            Assert.True(store.HasSecret(SecretKeys.AzureOpenAiApiKey));
            Assert.Equal("super-secret-key", store.GetSecret(SecretKeys.AzureOpenAiApiKey));

            var files = Directory.GetFiles(dir, "*.bin");
            Assert.Single(files);
            var onDisk = File.ReadAllBytes(files[0]);
            var asText = Encoding.UTF8.GetString(onDisk);
            Assert.DoesNotContain("super-secret-key", asText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void SecretBootstrap_MigratesFromConfigurationOnce()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"patchguard-secrets-{Guid.NewGuid():N}");
        try
        {
            var secrets = new DpapiSecretStorageService(dir);

            var migrated = SecretBootstrap.ResolveAndMigrate(
                secrets,
                SecretKeys.OpenAiApiKey,
                "sk-from-config");
            Assert.Equal("sk-from-config", migrated);
            Assert.Equal("sk-from-config", secrets.GetSecret(SecretKeys.OpenAiApiKey));

            secrets.SetSecret(SecretKeys.OpenAiApiKey, "sk-dpapi");
            var preferStore = SecretBootstrap.ResolveAndMigrate(
                secrets,
                SecretKeys.OpenAiApiKey,
                "sk-from-config");
            Assert.Equal("sk-dpapi", preferStore);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void SetSecret_EmptyDeletesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"patchguard-secrets-{Guid.NewGuid():N}");
        try
        {
            var store = new DpapiSecretStorageService(dir);
            store.SetSecret(SecretKeys.OpenAiApiKey, "temp");
            Assert.True(store.HasSecret(SecretKeys.OpenAiApiKey));
            store.SetSecret(SecretKeys.OpenAiApiKey, "  ");
            Assert.False(store.HasSecret(SecretKeys.OpenAiApiKey));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
