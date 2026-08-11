using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PatchGuard.Services.Ai;

namespace PatchGuard.Tests;

public sealed class ChatProviderResolverTests
{
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

    private static ChatProviderResolver CreateResolver(AiOptions options)
    {
        var openAi = new OpenAiChatClient(new HttpClient(new NoOpHandler()), options);
        var ollama = new OllamaChatProvider(new HttpClient(new NoOpHandler()), options);
        return new ChatProviderResolver(openAi, ollama, options);
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

public sealed class OllamaChatProviderTests
{
    [Fact]
    public async Task CompleteAsyncPostsChatPayloadAndReadsMessageContent()
    {
        var handler = new RecordingHandler();
        var options = new AiOptions
        {
            OllamaEnabled = true,
            OllamaBaseUrl = "http://localhost:11434",
            OllamaModel = "qwen3.5:latest"
        };
        var provider = new OllamaChatProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri(OllamaChatProvider.NormalizeBaseUrl(options.OllamaBaseUrl))
        }, options);

        var reply = await provider.CompleteAsync("system", "user question");

        Assert.Equal("hello from ollama", reply);
        Assert.Contains("/api/chat", handler.RequestUri, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(handler.Payload);
        Assert.Equal("qwen3.5:latest", doc.RootElement.GetProperty("model").GetString());
        Assert.False(doc.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task CompleteAsyncThrowsWhenEmptyContent()
    {
        var handler = new EmptyContentHandler();
        var options = new AiOptions { OllamaEnabled = true, OllamaModel = "qwen3.5:latest" };
        var provider = new OllamaChatProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434/")
        }, options);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
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
}
