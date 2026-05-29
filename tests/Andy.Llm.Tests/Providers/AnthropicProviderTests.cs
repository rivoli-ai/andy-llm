using System.Net;
using System.Text;
using System.Text.Json;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Unit coverage for <see cref="AnthropicProvider"/>. The provider is a
/// direct HttpClient implementation of the Anthropic Messages API; tests
/// stub <see cref="HttpMessageHandler"/> to assert wire-shape correctness
/// without hitting the real service. The opt-in integration test against
/// a real API key lives in <see cref="AnthropicIntegrationTests"/>.
/// </summary>
public class AnthropicProviderTests
{
    private readonly Mock<ILogger<AnthropicProvider>> _logger = new();

    private static ProviderConfig DefaultConfig() => new()
    {
        Provider = "anthropic",
        ApiKey = "sk-ant-test",
        ApiBase = "https://api.anthropic.example",
        Model = "claude-sonnet-4-5-20250929"
    };

    private static (AnthropicProvider, Mock<HttpMessageHandler>) BuildProvider(
        ILogger<AnthropicProvider> logger,
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond,
        ProviderConfig? config = null)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) => respond(req, ct));

        var http = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("Anthropic")).Returns(http);

        var provider = new AnthropicProvider(
            config ?? DefaultConfig(), "anthropic", logger, factory.Object);

        return (provider, handler);
    }

    [Fact]
    public void Constructor_MissingApiKey_Throws()
    {
        var config = DefaultConfig();
        config.ApiKey = null;
        Assert.Throws<InvalidOperationException>(
            () => new AnthropicProvider(config, "anthropic", _logger.Object));
    }

    [Fact]
    public void Constructor_MissingModel_Throws()
    {
        var config = DefaultConfig();
        config.Model = null;
        Assert.Throws<InvalidOperationException>(
            () => new AnthropicProvider(config, "anthropic", _logger.Object));
    }

    [Fact]
    public async Task CompleteAsync_NonStreaming_RoundTripsTextResponse()
    {
        HttpRequestMessage? captured = null;
        var (provider, _) = BuildProvider(_logger.Object, (req, _) =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{
                    ""id"":""msg_01"",
                    ""model"":""claude-sonnet-4-5-20250929"",
                    ""role"":""assistant"",
                    ""content"":[{""type"":""text"",""text"":""Hello, World!""}],
                    ""stop_reason"":""end_turn"",
                    ""usage"":{""input_tokens"":12,""output_tokens"":4}
                }", Encoding.UTF8, "application/json")
            };
        });

        var response = await provider.CompleteAsync(new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "Say hello" }
            },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929", MaxTokens = 32 }
        });

        Assert.NotNull(response);
        Assert.NotNull(response.AssistantMessage);
        Assert.Equal("Hello, World!", response.AssistantMessage!.Content);
        Assert.Equal("claude-sonnet-4-5-20250929", response.Model);
        Assert.Equal("end_turn", response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(12, response.Usage!.PromptTokens);
        Assert.Equal(4, response.Usage.CompletionTokens);
        Assert.Equal(16, response.Usage.TotalTokens);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/v1/messages", captured.RequestUri!.PathAndQuery);
        Assert.True(captured.Headers.TryGetValues("x-api-key", out var keys));
        Assert.Equal("sk-ant-test", keys!.Single());
        Assert.True(captured.Headers.TryGetValues("anthropic-version", out var versions));
        Assert.Equal(AnthropicProvider.DefaultAnthropicVersion, versions!.Single());

        // Body shape: messages array, system absent, max_tokens echoed.
        var body = await captured.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal("claude-sonnet-4-5-20250929", root.GetProperty("model").GetString());
        Assert.Equal(32, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.TryGetProperty("system", out _),
            "system prompt should be omitted when LlmRequest has no SystemPrompt");
        Assert.Equal(1, root.GetProperty("messages").GetArrayLength());
        var firstMsg = root.GetProperty("messages")[0];
        Assert.Equal("user", firstMsg.GetProperty("role").GetString());
        Assert.Equal("text", firstMsg.GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task CompleteAsync_SystemMessage_LiftedToTopLevelField()
    {
        HttpRequestMessage? captured = null;
        var (provider, _) = BuildProvider(_logger.Object, (req, _) =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{
                    ""content"":[{""type"":""text"",""text"":""ok""}],
                    ""stop_reason"":""end_turn"",
                    ""usage"":{""input_tokens"":1,""output_tokens"":1}
                }", Encoding.UTF8, "application/json")
            };
        });

        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.System, Content = "You are concise." },
                new() { Role = Role.User, Content = "Hi" }
            },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        });

        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("You are concise.", doc.RootElement.GetProperty("system").GetString());
        // The system message must NOT be inside the messages array.
        Assert.Equal(1, doc.RootElement.GetProperty("messages").GetArrayLength());
        Assert.Equal("user", doc.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task CompleteAsync_AlwaysSendsMaxTokens()
    {
        // Anthropic rejects requests without max_tokens with HTTP 400.
        // Verify max_tokens is always present and positive in the
        // serialized body, regardless of whether the caller specified
        // a value (in which case Andy.Model's LlmClientConfig supplies
        // a non-zero default).
        HttpRequestMessage? captured = null;
        var (provider, _) = BuildProvider(_logger.Object, (req, _) =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{
                    ""content"":[{""type"":""text"",""text"":""ok""}],
                    ""usage"":{""input_tokens"":1,""output_tokens"":1}
                }", Encoding.UTF8, "application/json")
            };
        });

        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "ping" } },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        });

        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("max_tokens", out var maxTokensProp));
        Assert.True(maxTokensProp.GetInt32() > 0, "max_tokens must be a positive integer (Anthropic 400s without it)");
    }

    [Fact]
    public async Task CompleteAsync_ToolDeclaration_SerializedAsInputSchema()
    {
        HttpRequestMessage? captured = null;
        var (provider, _) = BuildProvider(_logger.Object, (req, _) =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{
                    ""content"":[{
                        ""type"":""tool_use"",
                        ""id"":""toolu_01"",
                        ""name"":""get_weather"",
                        ""input"":{""location"":""SF""}
                    }],
                    ""stop_reason"":""tool_use"",
                    ""usage"":{""input_tokens"":10,""output_tokens"":5}
                }", Encoding.UTF8, "application/json")
            };
        });

        var response = await provider.CompleteAsync(new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "Weather in SF?" } },
            Tools = new List<ToolDeclaration>
            {
                new()
                {
                    Name = "get_weather",
                    Description = "Get current weather",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new Dictionary<string, object> { ["type"] = "string" }
                        }
                    }
                }
            },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929", MaxTokens = 64 }
        });

        // Request side: tools array contains our declaration with input_schema
        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var tools = doc.RootElement.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("get_weather", tools[0].GetProperty("name").GetString());
        Assert.Equal("object", tools[0].GetProperty("input_schema").GetProperty("type").GetString());

        // Response side: tool_use block surfaced as ToolCall on AssistantMessage
        Assert.NotNull(response.AssistantMessage!.ToolCalls);
        var call = response.AssistantMessage.ToolCalls!.Single();
        Assert.Equal("toolu_01", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Contains("SF", call.ArgumentsJson);
        Assert.Equal("tool_use", response.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_ToolResultMessage_BecomesToolResultBlock()
    {
        HttpRequestMessage? captured = null;
        var (provider, _) = BuildProvider(_logger.Object, (req, _) =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(@"{
                    ""content"":[{""type"":""text"",""text"":""72F""}],
                    ""usage"":{""input_tokens"":1,""output_tokens"":1}
                }", Encoding.UTF8, "application/json")
            };
        });

        await provider.CompleteAsync(new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "Weather in SF?" },
                new()
                {
                    Role = Role.Assistant,
                    ToolCalls = new List<ToolCall>
                    {
                        new() { Id = "toolu_01", Name = "get_weather", ArgumentsJson = "{\"location\":\"SF\"}" }
                    }
                },
                new() { Role = Role.Tool, ToolCallId = "toolu_01", Content = "{\"temp\":72}" }
            },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        });

        var body = await captured!.Content!.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        // Last message is a user-role message carrying a tool_result block.
        var toolResultMsg = messages[2];
        Assert.Equal("user", toolResultMsg.GetProperty("role").GetString());
        var block = toolResultMsg.GetProperty("content")[0];
        Assert.Equal("tool_result", block.GetProperty("type").GetString());
        Assert.Equal("toolu_01", block.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task CompleteAsync_4xx_ThrowsWithBodyDetail()
    {
        var (provider, _) = BuildProvider(_logger.Object, (_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(@"{""error"":{""type"":""invalid_request_error"",""message"":""max_tokens required""}}",
                    Encoding.UTF8, "application/json")
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync(new LlmRequest
            {
                Messages = new List<Message> { new() { Role = Role.User, Content = "x" } },
                Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
            }));

        Assert.Contains("400", ex.Message);
        Assert.Contains("max_tokens required", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_5xx_ThrowsWithBodyDetail()
    {
        var (provider, _) = BuildProvider(_logger.Object, (_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal server error", Encoding.UTF8, "text/plain")
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync(new LlmRequest
            {
                Messages = new List<Message> { new() { Role = Role.User, Content = "x" } },
                Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
            }));

        Assert.Contains("500", ex.Message);
        Assert.Contains("Internal server error", ex.Message);
    }

    [Fact]
    public async Task StreamCompleteAsync_TextDeltas_YieldChunksAndFinalCompletion()
    {
        // Build a synthetic SSE stream covering the canonical sequence:
        // message_start → content_block_start → content_block_delta(text) ×N
        // → content_block_stop → message_delta(stop_reason) → message_stop.
        var sse = string.Join("\n\n", new[]
        {
            "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\",\"model\":\"claude-sonnet-4-5-20250929\",\"usage\":{\"input_tokens\":5,\"output_tokens\":0}}}",
            "event: content_block_start\ndata: {\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}",
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}",
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"lo\"}}",
            "event: content_block_stop\ndata: {\"type\":\"content_block_stop\",\"index\":0}",
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":2}}",
            "event: message_stop\ndata: {\"type\":\"message_stop\"}",
            "" // trailing blank
        });

        var (provider, _) = BuildProvider(_logger.Object, (_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            });

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "say hello" } },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        }))
        {
            chunks.Add(chunk);
        }

        // Two text deltas + one final completion frame.
        var textChunks = chunks.Where(c => !c.IsComplete).ToList();
        Assert.Equal(2, textChunks.Count);
        Assert.Equal("Hel", textChunks[0].Delta!.Content);
        Assert.Equal("lo", textChunks[1].Delta!.Content);

        var completion = chunks.Single(c => c.IsComplete);
        Assert.Equal("end_turn", completion.FinishReason);
        Assert.NotNull(completion.Usage);
        Assert.Equal(5, completion.Usage!.PromptTokens);
        Assert.Equal(2, completion.Usage.CompletionTokens);
    }

    [Fact]
    public async Task StreamCompleteAsync_MalformedSseLine_Skipped()
    {
        // A garbage data line interleaved with a real text_delta should not
        // crash the consumer; the bad line is silently dropped, the good
        // one is yielded, and the stream terminates cleanly.
        var sse =
            "event: content_block_delta\ndata: {{{not valid json\n\n" +
            "event: content_block_delta\ndata: {\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"survives\"}}\n\n" +
            "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";

        var (provider, _) = BuildProvider(_logger.Object, (_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            });

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "x" } },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        }))
        {
            chunks.Add(chunk);
        }

        Assert.Contains(chunks, c => c.Delta?.Content == "survives");
        Assert.Single(chunks, c => c.IsComplete);
    }

    [Fact]
    public async Task StreamCompleteAsync_4xxErrorBeforeStream_YieldsErrorFrame()
    {
        var (provider, _) = BuildProvider(_logger.Object, (_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(@"{""error"":{""message"":""invalid api key""}}",
                    Encoding.UTF8, "application/json")
            });

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new List<Message> { new() { Role = Role.User, Content = "x" } },
            Config = new LlmClientConfig { Model = "claude-sonnet-4-5-20250929" }
        }))
        {
            chunks.Add(chunk);
        }

        var error = Assert.Single(chunks);
        Assert.NotNull(error.Error);
        Assert.Contains("401", error.Error);
        Assert.Contains("invalid api key", error.Error);
    }

    [Fact]
    public async Task ListModelsAsync_ReturnsHardCodedClaudeFamily()
    {
        // Anthropic exposes no /models endpoint, so the list is static.
        // We assert the snapshot contains the current marquee model
        // (claude-opus-4-5) and that everything is correctly attributed.
        var provider = new AnthropicProvider(DefaultConfig(), "anthropic", _logger.Object);
        var models = (await provider.ListModelsAsync()).ToList();

        Assert.NotEmpty(models);
        Assert.All(models, m =>
        {
            Assert.Equal("anthropic", m.Provider);
            Assert.Equal("Claude", m.Family);
            Assert.True(m.SupportsFunctions);
        });
        Assert.Contains(models, m => m.Id == "claude-opus-4-5-20251101");
        Assert.Contains(models, m => m.Id == "claude-sonnet-4-5-20250929");
    }

    [Fact]
    public void Factory_RoutesAnthropicProviderType()
    {
        // Wire-level smoke for the LlmProviderFactory: declaring a
        // provider with Provider="anthropic" should resolve to
        // AnthropicProvider, not throw NotSupportedException as before.
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(loggerFactory.Object);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["anthropic"] = new ProviderConfig
                {
                    Provider = "anthropic",
                    ApiKey = "sk-ant-test",
                    Model = "claude-sonnet-4-5-20250929",
                    Enabled = true
                }
            },
            DefaultProvider = "anthropic"
        });

        var factory = new LlmProviderFactory(
            sp, options, new Mock<ILogger<LlmProviderFactory>>().Object);

        var provider = factory.CreateProvider("anthropic");
        Assert.IsType<AnthropicProvider>(provider);
    }
}
