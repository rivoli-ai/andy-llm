using System.Net;
using System.Text;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Unit coverage for <see cref="OpenRouterProvider"/>. OpenRouter speaks the
/// OpenAI chat-completions wire format, so these tests stub
/// <see cref="HttpMessageHandler"/> to assert wire-shape correctness without
/// hitting the real service.
/// </summary>
public class OpenRouterProviderTests
{
    private const string OpenRouterBase = "https://openrouter.ai/api/v1";

    private static ProviderConfig ValidConfig() => new()
    {
        Provider = "openrouter",
        ApiKey = "sk-or-test",
        ApiBase = OpenRouterBase,
        Model = "anthropic/claude-sonnet-4.6",
        Enabled = true
    };

    private static (OpenRouterProvider provider, FakeHandler handler) Build(ProviderConfig? config = null)
    {
        var handler = new FakeHandler();
        var factory = new FakeHttpClientFactory(handler);
        var provider = new OpenRouterProvider(config ?? ValidConfig(), "openrouter", NullLogger<OpenRouterProvider>.Instance, factory);
        return (provider, handler);
    }

    [Fact]
    public void Constructor_WithMissingApiKey_Throws()
    {
        var cfg = ValidConfig();
        cfg.ApiKey = null;
        Assert.Throws<InvalidOperationException>(() => new OpenRouterProvider(cfg, "openrouter", NullLogger<OpenRouterProvider>.Instance));
    }

    [Fact]
    public void Constructor_WithMissingModel_Throws()
    {
        var cfg = ValidConfig();
        cfg.Model = null;
        Assert.Throws<InvalidOperationException>(() => new OpenRouterProvider(cfg, "openrouter", NullLogger<OpenRouterProvider>.Instance));
    }

    [Fact]
    public void Constructor_WithMissingApiBase_DefaultsToOpenRouterEndpoint()
    {
        // Unlike the Gateway provider, ApiBase is optional — OpenRouter's
        // endpoint is fixed, so an absent base must not throw.
        var cfg = ValidConfig();
        cfg.ApiBase = null;
        var ex = Record.Exception(() => new OpenRouterProvider(cfg, "openrouter", NullLogger<OpenRouterProvider>.Instance));
        Assert.Null(ex);
    }

    [Fact]
    public void Name_IsConfigName()
    {
        var (provider, _) = Build();
        Assert.Equal("openrouter", provider.Name);
    }

    [Fact]
    public async Task IsAvailableAsync_When200OnModels_ReturnsTrue()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json")
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
                """{"id":"gen-1","model":"anthropic/claude-sonnet-4.6","choices":[{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":1,"total_tokens":5}}""",
                Encoding.UTF8, "application/json")
        });

        var response = await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
        });

        Assert.Equal("hi", response.Content);
        Assert.Equal("stop", response.FinishReason);
        Assert.Equal("anthropic/claude-sonnet-4.6", response.Model);
        Assert.NotNull(response.Usage);
        Assert.Equal(4, response.Usage!.PromptTokens);
        Assert.Equal(1, response.Usage.CompletionTokens);
        Assert.Equal(5, response.Usage.TotalTokens);

        var sent = Assert.Single(handler.Calls);
        Assert.Equal("Bearer", sent.Request.Headers.Authorization?.Scheme);
        Assert.Equal("sk-or-test", sent.Request.Headers.Authorization?.Parameter);
        Assert.EndsWith("/chat/completions", sent.Request.RequestUri!.AbsolutePath);
        // The slashed OpenRouter model id must round-trip unmangled in the body.
        Assert.Contains("\"model\":\"anthropic/claude-sonnet-4.6\"", sent.Body);
        Assert.Contains("\"messages\":[", sent.Body);
    }

    // --- ExtraBody passthrough: OpenRouter provider routing, model fallbacks, and arbitrary fields ---

    private static void EnqueueOk(FakeHandler handler) =>
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""",
                Encoding.UTF8, "application/json")
        });

    [Fact]
    public async Task ExtraBody_ProviderRouting_AppearsVerbatimInBody()
    {
        var (provider, handler) = Build();
        EnqueueOk(handler);
        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } },
            ExtraBody = new Dictionary<string, object?>
            {
                ["provider"] = new Dictionary<string, object?>
                {
                    ["order"] = new[] { "deepinfra/turbo" },
                    ["allow_fallbacks"] = false
                }
            }
        });
        var body = Assert.Single(handler.Calls).Body;
        // Specific-provider routing: the nested object serializes as JSON (not ToString()).
        Assert.Contains("\"provider\":{", body);
        Assert.Contains("\"order\":[\"deepinfra/turbo\"]", body);
        Assert.Contains("\"allow_fallbacks\":false", body);
    }

    [Fact]
    public async Task ExtraBody_ModelsFallbackArray_AppearsInBody()
    {
        var (provider, handler) = Build();
        EnqueueOk(handler);
        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } },
            ExtraBody = new Dictionary<string, object?>
            {
                ["models"] = new[] { "deepseek/deepseek-r1", "openai/gpt-5" }
            }
        });
        var body = Assert.Single(handler.Calls).Body;
        Assert.Contains("\"models\":[\"deepseek/deepseek-r1\",\"openai/gpt-5\"]", body);
    }

    [Fact]
    public async Task ExtraBody_ArbitraryScalarField_AppearsInBody()
    {
        var (provider, handler) = Build();
        EnqueueOk(handler);
        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } },
            ExtraBody = new Dictionary<string, object?>
            {
                ["reasoning"] = new Dictionary<string, object?> { ["effort"] = "high" }
            }
        });
        var body = Assert.Single(handler.Calls).Body;
        Assert.Contains("\"reasoning\":{\"effort\":\"high\"}", body);
    }

    [Fact]
    public async Task ExtraBody_Absent_BodyUnchanged()
    {
        var (provider, handler) = Build();
        EnqueueOk(handler);
        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
        });
        var body = Assert.Single(handler.Calls).Body;
        Assert.DoesNotContain("\"provider\":", body);
        Assert.DoesNotContain("\"models\":", body);
    }

    [Fact]
    public async Task ExtraBody_WinsOnCollisionWithStandardField()
    {
        var (provider, handler) = Build();
        EnqueueOk(handler);
        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "hi" } },
            Config = new LlmClientConfig { Temperature = 0.9m },
            ExtraBody = new Dictionary<string, object?> { ["temperature"] = 0.1 }
        });
        var body = Assert.Single(handler.Calls).Body;
        Assert.Contains("\"temperature\":0.1", body);
        Assert.DoesNotContain("0.9", body);
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
        Assert.Contains("\"model\":\"anthropic/claude-sonnet-4.6\"", sent.Body);
    }

    [Fact]
    public async Task CompleteAsync_NonSuccess_ThrowsWithStatusAndBody()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent(
                """{"error":{"message":"Insufficient credits","code":402}}""",
                Encoding.UTF8, "application/json")
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync(new LlmRequest
            {
                Messages = new[] { new Message { Role = Role.User, Content = "hi" } }
            }));

        Assert.Contains("402", ex.Message);
        Assert.Contains("Insufficient credits", ex.Message);
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
            Messages = new[] { new Message { Role = Role.User, Content = "weather?" } },
            Tools = new List<ToolDeclaration>
            {
                new()
                {
                    Name = "get_weather",
                    Description = "Get current weather",
                    Parameters = new Dictionary<string, object> { ["type"] = "object" }
                }
            }
        });

        Assert.Single(response.ToolCalls);
        Assert.Equal("get_weather", response.ToolCalls[0].Name);
        Assert.Equal("call-1", response.ToolCalls[0].Id);
        Assert.Contains("Paris", response.ToolCalls[0].ArgumentsJson);
        Assert.Equal("tool_calls", response.FinishReason);

        // The tool declaration must be forwarded in OpenAI function-tool shape.
        var sent = Assert.Single(handler.Calls);
        Assert.Contains("\"tools\":[", sent.Body);
        Assert.Contains("\"name\":\"get_weather\"", sent.Body);
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

        var sent = Assert.Single(handler.Calls);
        Assert.Contains("\"stream\":true", sent.Body);
    }

    [Fact]
    public async Task StreamCompleteAsync_NonSuccess_YieldsErrorFrame()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"message":"No auth credentials found"}}""",
                Encoding.UTF8, "application/json")
        });

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new[] { new Message { Role = Role.User, Content = "x" } }
        }))
        {
            chunks.Add(chunk);
        }

        var error = Assert.Single(chunks);
        Assert.True(error.IsComplete);
        Assert.NotNull(error.Error);
        Assert.Contains("401", error.Error);
        Assert.Contains("No auth credentials found", error.Error);
    }

    [Fact]
    public async Task ListModelsAsync_ParsesDataArray_AndDerivesProviderFromIdPrefix()
    {
        var (provider, handler) = Build();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":[{"id":"openai/gpt-5.2","name":"GPT-5.2"},{"id":"anthropic/claude-sonnet-4.6","name":"Claude Sonnet 4.6"}]}""",
                Encoding.UTF8, "application/json")
        });

        var models = (await provider.ListModelsAsync()).ToList();
        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Id == "openai/gpt-5.2" && m.Provider == "openai" && m.Name == "GPT-5.2");
        Assert.Contains(models, m => m.Id == "anthropic/claude-sonnet-4.6" && m.Provider == "anthropic");
    }

    [Fact]
    public async Task Constructor_RankingHeaders_SentFromAdditionalSettings()
    {
        var cfg = ValidConfig();
        cfg.AdditionalSettings = new Dictionary<string, object>
        {
            ["HttpReferer"] = "https://andy.example",
            ["XTitle"] = "Andy"
        };
        var (provider, handler) = Build(cfg);
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
        Assert.True(sent.Request.Headers.TryGetValues("HTTP-Referer", out var referer));
        Assert.Equal("https://andy.example", referer!.Single());
        Assert.True(sent.Request.Headers.TryGetValues("X-Title", out var title));
        Assert.Equal("Andy", title!.Single());
    }

    [Fact]
    public void Factory_RoutesOpenRouterProviderType()
    {
        // The slashed config key ("openrouter/sonnet") must split to the
        // "openrouter" provider type while the slashed model id stays in Model.
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory.Object);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openrouter/sonnet"] = new ProviderConfig
                {
                    Provider = "openrouter",
                    ApiKey = "sk-or-test",
                    ApiBase = OpenRouterBase,
                    Model = "anthropic/claude-sonnet-4.6",
                    Enabled = true
                }
            },
            DefaultProvider = "openrouter/sonnet"
        });

        var factory = new LlmProviderFactory(
            sp, options, new Mock<ILogger<LlmProviderFactory>>().Object);

        var provider = factory.CreateProvider("openrouter/sonnet");
        Assert.IsType<OpenRouterProvider>(provider);
        Assert.Equal("openrouter/sonnet", provider.Name);
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
