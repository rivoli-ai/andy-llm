using System.Net;
using System.Text;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Llm.Tests.Providers;

public class GatewayProviderTests
{
    private const string GatewayBase = "https://localhost:5120";

    private static ProviderConfig ValidConfig() => new()
    {
        Provider = "gateway",
        ApiKey = "test-token",
        ApiBase = GatewayBase,
        Model = "openai/gpt-4o-mini",
        Enabled = true
    };

    private static (GatewayProvider provider, FakeHandler handler) Build(ProviderConfig? config = null)
    {
        var handler = new FakeHandler();
        var factory = new FakeHttpClientFactory(handler);
        var provider = new GatewayProvider(config ?? ValidConfig(), "gateway", NullLogger<GatewayProvider>.Instance, factory);
        return (provider, handler);
    }

    [Fact]
    public void Constructor_WithMissingApiKey_Throws()
    {
        var cfg = ValidConfig();
        cfg.ApiKey = null;
        Assert.Throws<InvalidOperationException>(() => new GatewayProvider(cfg, "gateway", NullLogger<GatewayProvider>.Instance));
    }

    [Fact]
    public void Constructor_WithMissingApiBase_Throws()
    {
        var cfg = ValidConfig();
        cfg.ApiBase = null;
        Assert.Throws<InvalidOperationException>(() => new GatewayProvider(cfg, "gateway", NullLogger<GatewayProvider>.Instance));
    }

    [Fact]
    public void Constructor_WithMissingModel_Throws()
    {
        var cfg = ValidConfig();
        cfg.Model = null;
        Assert.Throws<InvalidOperationException>(() => new GatewayProvider(cfg, "gateway", NullLogger<GatewayProvider>.Instance));
    }

    [Fact]
    public void Name_IsConfigName()
    {
        var (provider, _) = Build();
        Assert.Equal("gateway", provider.Name);
    }

    [Fact]
    public async Task IsAvailableAsync_When200OnModels_ReturnsTrue()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"object":"list","data":[]}""", Encoding.UTF8, "application/json")
        });

        Assert.True(await provider.IsAvailableAsync());
    }

    [Fact]
    public async Task IsAvailableAsync_WhenUnreachable_ReturnsFalse()
    {
        var (provider, handler) = Build();
        handler.EnqueueThrow(new HttpRequestException("dns"));

        Assert.False(await provider.IsAvailableAsync());
    }

    [Fact]
    public async Task CompleteAsync_SendsBearerToken_AndParsesResponse()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"id":"x","model":"openai/gpt-4o-mini","choices":[{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":1,"total_tokens":5}}""",
                Encoding.UTF8, "application/json")
        });

        var response = await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
        });

        Assert.Equal("hi", response.Content);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal("openai/gpt-4o-mini", response.Model);
        Assert.NotNull(response.Usage);
        Assert.Equal(4, response.Usage!.PromptTokens);
        Assert.Equal(1, response.Usage.CompletionTokens);
        Assert.Equal(5, response.Usage.TotalTokens);

        var sent = Assert.Single(handler.Calls);
        Assert.Equal("Bearer", sent.Request.Headers.Authorization?.Scheme);
        Assert.Equal("test-token", sent.Request.Headers.Authorization?.Parameter);
        Assert.Contains("\"model\":\"openai/gpt-4o-mini\"", sent.Body);
        Assert.Contains("\"messages\":[", sent.Body);
    }

    [Fact]
    public async Task CompleteAsync_RequestDefaultsModelFromConfigWhenMissing()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                Encoding.UTF8, "application/json")
        });

        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
        });

        var sent = Assert.Single(handler.Calls);
        Assert.Contains("\"model\":\"openai/gpt-4o-mini\"", sent.Body);
    }

    [Fact]
    public async Task CompleteAsync_ParsesToolCallsFromResponse()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"","tool_calls":[{"id":"call-1","type":"function","function":{"name":"get_weather","arguments":"{\"city\":\"Paris\"}"}}]},"finish_reason":"tool_calls"}]}""",
                Encoding.UTF8, "application/json")
        });

        var response = await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "weather?" } }
        });

        Assert.Single(response.ToolCalls);
        Assert.Equal("get_weather", response.ToolCalls[0].Name);
        Assert.Equal("call-1", response.ToolCalls[0].Id);
        Assert.Contains("Paris", response.ToolCalls[0].ArgumentsJson);
        Assert.Equal("tool_calls", response.FinishReason);
    }

    [Fact]
    public async Task StreamCompleteAsync_YieldsDeltas_AndFinalUsage()
    {
        var (provider, handler) = Build();
        var sse =
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"hi\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":5}}\n\n" +
            "data: [DONE]\n\n";
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        });

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
        }))
        {
            chunks.Add(chunk);
        }

        var content = string.Concat(chunks.Where(c => c.Delta is not null).Select(c => c.Delta!.Content));
        Assert.Equal("hi world", content);

        var terminal = chunks.Last();
        Assert.True(terminal.IsComplete);
        Assert.Equal("stop", terminal.FinishReason);
        Assert.NotNull(terminal.Usage);
        Assert.Equal(3, terminal.Usage!.PromptTokens);
        Assert.Equal(2, terminal.Usage.CompletionTokens);
        Assert.Equal(5, terminal.Usage.TotalTokens);

        // stream:true must be set on the outgoing request.
        var sent = Assert.Single(handler.Calls);
        Assert.Contains("\"stream\":true", sent.Body);
    }

    [Fact]
    public async Task ListModelsAsync_ParsesDataArray()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"object":"list","data":[{"id":"openai/gpt-4o-mini","object":"model","owned_by":"openai"},{"id":"anthropic/claude-sonnet-4-5","object":"model","owned_by":"anthropic"}]}""",
                Encoding.UTF8, "application/json")
        });

        var models = (await provider.ListModelsAsync()).ToList();
        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "openai/gpt-4o-mini" && m.Provider == "openai");
        Assert.Contains(models, m => m.Id == "anthropic/claude-sonnet-4-5" && m.Provider == "anthropic");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();

        public void Enqueue(HttpResponseMessage response) => _responders.Enqueue(_ => response);
        public void EnqueueThrow(Exception ex) => _responders.Enqueue(_ => throw ex);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            if (_responders.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            return _responders.Dequeue()(request);
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
