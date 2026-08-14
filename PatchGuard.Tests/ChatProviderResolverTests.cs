using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class ChatProviderResolverTests
{
    [Fact]
    public void AutoPrefersAzureOverOpenAiWhenBothConfigured()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            AzureApiKey = "azure-key",
            AzureEndpoint = "https://example.openai.azure.com/",
            AzureDeployment = "gpt-deploy",
            ChatProvider = ChatProviderResolver.ModeAuto,
            OllamaEnabled = true
        };
        var resolver = CreateResolver(options);

        var provider = resolver.Resolve(allowExternalServices: true);

        Assert.NotNull(provider);
        Assert.Equal(AzureOpenAiChatProvider.ProviderName, provider.Name);
    }

    [Fact]
    public void AzureModeRequiresConsent()
    {
        var options = new AiOptions
        {
            AzureApiKey = "azure-key",
            AzureEndpoint = "https://example.openai.azure.com/",
            AzureDeployment = "gpt-deploy",
            ChatProvider = ChatProviderResolver.ModeAzure
        };
        var resolver = CreateResolver(options);

        Assert.Null(resolver.Resolve(allowExternalServices: false));
        Assert.Equal(AzureOpenAiChatProvider.ProviderName, resolver.Resolve(allowExternalServices: true)!.Name);
    }

    [Fact]
    public void AutoPrefersOpenAiWhenConsentAndKeyPresent()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            ChatProvider = ChatProviderResolver.ModeAuto,
            OllamaEnabled = true
        };
        var resolver = CreateResolver(options);

        var provider = resolver.Resolve(allowExternalServices: true);

        Assert.NotNull(provider);
        Assert.Equal(OpenAiChatClient.ProviderName, provider.Name);
    }

    [Fact]
    public void AutoUsesOllamaWithoutConsentWhenEnabled()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            ChatProvider = ChatProviderResolver.ModeAuto,
            OllamaEnabled = true
        };
        var resolver = CreateResolver(options);

        var provider = resolver.Resolve(allowExternalServices: false);

        Assert.NotNull(provider);
        Assert.Equal(OllamaChatProvider.ProviderName, provider.Name);
    }

    [Fact]
    public void RulesModeAlwaysReturnsNull()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            ChatProvider = ChatProviderResolver.ModeRules,
            OllamaEnabled = true
        };
        var resolver = CreateResolver(options);

        Assert.Null(resolver.Resolve(allowExternalServices: true));
    }

    [Fact]
    public void OpenAiModeRequiresConsent()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            ChatProvider = ChatProviderResolver.ModeOpenAi
        };
        var resolver = CreateResolver(options);

        Assert.Null(resolver.Resolve(allowExternalServices: false));
        Assert.Equal(OpenAiChatClient.ProviderName, resolver.Resolve(allowExternalServices: true)!.Name);
    }

    [Fact]
    public void OllamaModeIgnoresOpenAiKey()
    {
        var options = new AiOptions
        {
            ApiKey = "sk-test",
            ChatProvider = ChatProviderResolver.ModeOllama,
            OllamaEnabled = true
        };
        var resolver = CreateResolver(options);

        Assert.Equal(OllamaChatProvider.ProviderName, resolver.Resolve(allowExternalServices: true)!.Name);
    }

    [Fact]
    public void DisabledOllamaIsUnavailable()
    {
        var options = new AiOptions
        {
            ChatProvider = ChatProviderResolver.ModeOllama,
            OllamaEnabled = false
        };
        var resolver = CreateResolver(options);

        Assert.Null(resolver.Resolve(allowExternalServices: false));
    }

    [Fact]
    public void RemoteOllamaIsUnavailableWithoutConsent()
    {
        var options = new AiOptions
        {
            ChatProvider = ChatProviderResolver.ModeOllama,
            OllamaEnabled = true,
            OllamaBaseUrl = "https://ollama.example.com",
            OllamaModel = "llama3.2:3b"
        };
        var resolver = CreateResolver(options);

        Assert.Null(resolver.Resolve(allowExternalServices: false));
    }

    private static ChatProviderResolver CreateResolver(AiOptions options)
    {
        var openAi = new OpenAiChatClient(new HttpClient(new NoOpHandler()), options);
        var azure = new AzureOpenAiChatProvider(new HttpClient(new NoOpHandler()), options);
        var ollama = new OllamaChatProvider(new HttpClient(new NoOpHandler()), options);
        return new ChatProviderResolver(openAi, azure, ollama, options);
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
}

public sealed class OpenAiChatClientTests
{
    [Fact]
    public async Task CompleteAsyncRejectsOversizedContentLengthResponse()
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
            ApiKey = "sk-test",
            Model = "test-model"
        };
        var provider = new OpenAiChatClient(
            new HttpClient(new StaticResponseHandler(
                new HeadersReadOnlyContent(oversizedPayload, includeContentLength: true))),
            options);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.CompleteAsync("system", "user"));
    }

    private sealed class StaticResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class HeadersReadOnlyContent : HttpContent
    {
        private readonly byte[] _bytes;

        public HeadersReadOnlyContent(string payload, bool includeContentLength)
        {
            _bytes = Encoding.UTF8.GetBytes(payload);
            Headers.ContentType = new("application/json");
            if (includeContentLength)
            {
                Headers.ContentLength = _bytes.Length;
            }
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            throw new InvalidOperationException("Response content must not be buffered.");

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return Headers.ContentLength.HasValue;
        }
    }
}

public sealed class OllamaChatProviderTests
{
    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://localhost", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://127.42.1.9:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://[::ffff:127.42.1.9]:11434", true)]
    [InlineData("https://ollama.example.com", false)]
    [InlineData("http://192.168.1.10:11434", false)]
    [InlineData("ftp://localhost:11434", false)]
    [InlineData("http://@localhost:11434", false)]
    [InlineData("http://user@localhost:11434", false)]
    [InlineData("http://localhost:11434?", false)]
    [InlineData("http://localhost:11434?model=test", false)]
    [InlineData("http://localhost:11434/#", false)]
    [InlineData("http://localhost:11434/#fragment", false)]
    public void LoopbackPolicyAcceptsOnlyHttpLoopbackEndpoints(string endpoint, bool expected)
    {
        var options = new AiOptions
        {
            OllamaEnabled = true,
            OllamaBaseUrl = endpoint,
            OllamaModel = "llama3.2:3b"
        };
        using var client = new HttpClient();
        var provider = new OllamaChatProvider(client, options);

        Assert.Equal(expected, provider.IsAvailable);
    }

    [Fact]
    public async Task CompleteAsyncPostsChatPayloadAndReadsMessageContent()
    {
        var handler = new RecordingHandler();
        var options = new AiOptions
        {
            OllamaEnabled = true,
            OllamaBaseUrl = "http://localhost:11434",
            OllamaModel = "llama3.2:3b"
        };
        var provider = new OllamaChatProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri(OllamaChatProvider.NormalizeBaseUrl(options.OllamaBaseUrl))
        }, options);

        var reply = await provider.CompleteAsync("system", "user question");

        Assert.Equal("hello from ollama", reply);
        Assert.Contains("/api/chat", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(handler.Payload);
        Assert.Equal("llama3.2:3b", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(512, doc.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task CompleteAsyncThrowsWhenEmptyContent()
    {
        var handler = new EmptyContentHandler();
        var options = new AiOptions { OllamaEnabled = true, OllamaModel = "llama3.2:3b" };
        var provider = new OllamaChatProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        }, options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync("system", "user"));
    }

    [Fact]
    public async Task CompleteAsyncRejectsOversizedUnknownLengthResponse()
    {
        var oversizedPayload = JsonSerializer.Serialize(new
        {
            message = new { role = "assistant", content = new string('x', 1_100_000) }
        });
        var options = new AiOptions
        {
            OllamaEnabled = true,
            OllamaBaseUrl = "http://localhost:11434",
            OllamaModel = "llama3.2:3b"
        };
        var provider = new OllamaChatProvider(
            new HttpClient(new UnknownLengthResponseHandler(oversizedPayload)),
            options);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            provider.CompleteAsync("system", "user"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Payload { get; private set; } = string.Empty;
        public string RequestUri { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString() ?? string.Empty;
            Payload = await request.Content!.ReadAsStringAsync(cancellationToken);
            var body = JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = "hello from ollama" }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class EmptyContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = "   " }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
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
