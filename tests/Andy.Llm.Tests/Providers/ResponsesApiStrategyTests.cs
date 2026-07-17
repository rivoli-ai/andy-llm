using System.Net;
using System.Text;
using System.Text.Json;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Tests for the ResponsesApiStrategy using mocked HTTP responses.
/// Verifies request building, response parsing, tool call handling,
/// and error handling for the OpenAI Responses API.
/// </summary>
public class ResponsesApiStrategyTests
{
    private readonly Mock<ILogger> _mockLogger = new();

    private ResponsesApiStrategy CreateStrategy(HttpMessageHandler handler, string model = "codex-mini-latest")
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/")
        };

        return new ResponsesApiStrategy(httpClient, model, _mockLogger.Object);
    }

    #region CompleteAsync Tests

    [Fact]
    public async Task CompleteAsync_SimpleTextResponse_ParsesCorrectly()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "resp_123",
            status = "completed",
            output = new object[]
            {
                new
                {
                    type = "message",
                    role = "assistant",
                    content = new object[]
                    {
                        new { type = "output_text", text = "Hello, world!" }
                    }
                }
            },
            output_text = "Hello, world!",
            model = "codex-mini-latest",
            usage = new { input_tokens = 10, output_tokens = 5, total_tokens = 15 }
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Hello" }
            },
            SystemPrompt = "You are helpful."
        };

        var response = await strategy.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.Equal("Hello, world!", response.AssistantMessage.Content);
        Assert.Equal(Role.Assistant, response.AssistantMessage.Role);
        Assert.Equal("stop", response.FinishReason);
        Assert.NotNull(response.Usage);
        Assert.Equal(10, response.Usage.PromptTokens);
        Assert.Equal(5, response.Usage.CompletionTokens);
        Assert.Equal(15, response.Usage.TotalTokens);
        Assert.Equal("codex-mini-latest", response.Model);
    }

    [Fact]
    public async Task CompleteAsync_WithToolCalls_ParsesFunctionCalls()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "resp_456",
            status = "completed",
            output = new object[]
            {
                new
                {
                    type = "function_call",
                    call_id = "call_abc123",
                    name = "read_file",
                    arguments = "{\"path\":\"/tmp/test.txt\"}"
                }
            },
            model = "codex-mini-latest",
            usage = new { input_tokens = 20, output_tokens = 15, total_tokens = 35 }
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Read the file" }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "read_file",
                    Description = "Read a file",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object> { ["type"] = "string" }
                        }
                    }
                }
            }
        };

        var response = await strategy.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.Equal("tool_calls", response.FinishReason);
        Assert.NotNull(response.AssistantMessage.ToolCalls);
        Assert.Single(response.AssistantMessage.ToolCalls);

        var toolCall = response.AssistantMessage.ToolCalls[0];
        Assert.Equal("call_abc123", toolCall.Id);
        Assert.Equal("read_file", toolCall.Name);
        Assert.Equal("{\"path\":\"/tmp/test.txt\"}", toolCall.ArgumentsJson);
    }

    [Fact]
    public async Task CompleteAsync_MultipleToolCalls_ParsesAll()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "resp_789",
            status = "completed",
            output = new object[]
            {
                new
                {
                    type = "function_call",
                    call_id = "call_1",
                    name = "read_file",
                    arguments = "{\"path\":\"/tmp/a.txt\"}"
                },
                new
                {
                    type = "function_call",
                    call_id = "call_2",
                    name = "write_file",
                    arguments = "{\"path\":\"/tmp/b.txt\",\"content\":\"hello\"}"
                }
            },
            model = "codex-mini-latest"
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "Copy file" } }
        };

        var response = await strategy.CompleteAsync(request);

        Assert.Equal(2, response.AssistantMessage.ToolCalls.Count);
        Assert.Equal("read_file", response.AssistantMessage.ToolCalls[0].Name);
        Assert.Equal("write_file", response.AssistantMessage.ToolCalls[1].Name);
    }

    [Fact]
    public async Task CompleteAsync_HttpError_ThrowsInvalidOperationException()
    {
        // Pre-fix behaviour: returned a synthesised LlmResponse with the
        // error in Content and FinishReason="error", which downstream
        // (Andy.Engine.SimpleAgent → andy-cli AQ3) would treat as a
        // successful agent response and write to the output file. The
        // contract is now: surface the failure so callers can distinguish
        // a transport error from a real model response.
        var handler = new MockHttpHandler(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"Model not found\"}}");
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "Hello" } }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.CompleteAsync(request));
        Assert.Contains("404", ex.Message);
        Assert.Contains("Model not found", ex.Message);
    }

    [Fact]
    public async Task CompleteAsync_TransportException_ThrowsInvalidOperationException()
    {
        // SDK-layer / network exceptions are wrapped so callers receive a
        // single exception type per the AzureOpenAIProvider convention.
        var handler = new ThrowingHttpHandler(new HttpRequestException("dns failure"));
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "Hello" } }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.CompleteAsync(request));
        Assert.Contains("dns failure", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task CompleteAsync_FailedStatus_ReturnsErrorFinishReason()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            id = "resp_err",
            status = "failed",
            output = Array.Empty<object>(),
            model = "codex-mini-latest"
        });

        var handler = new MockHttpHandler(HttpStatusCode.OK, responseJson);
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "test" } }
        };

        var response = await strategy.CompleteAsync(request);

        Assert.Equal("error", response.FinishReason);
    }

    #endregion

    #region Request Building Tests

    [Fact]
    public async Task CompleteAsync_SendsCorrectRequestFormat()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "resp_1",
            status = "completed",
            output = new object[]
            {
                new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "ok" } } }
            },
            output_text = "ok",
            model = "codex-mini-latest"
        }));

        var strategy = CreateStrategy(handler, "codex-mini-latest");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Hello" }
            },
            SystemPrompt = "Be helpful",
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration { Name = "test_tool", Description = "A test", Parameters = new Dictionary<string, object>() }
            }
        };

        await strategy.CompleteAsync(request);

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody);
        var root = doc.RootElement;

        Assert.Equal("codex-mini-latest", root.GetProperty("model").GetString());
        Assert.Equal("Be helpful", root.GetProperty("instructions").GetString());

        var input = root.GetProperty("input");
        Assert.Equal(JsonValueKind.Array, input.ValueKind);
        Assert.True(input.GetArrayLength() > 0);

        var tools = root.GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("test_tool", tools[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithToolCallHistory_SendsFunctionCallAndOutput()
    {
        var handler = new CapturingHttpHandler(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "resp_2",
            status = "completed",
            output = new object[]
            {
                new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "File says hello" } } }
            },
            output_text = "File says hello",
            model = "codex-mini-latest"
        }));

        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Read the file" },
                new Message
                {
                    Role = Role.Assistant,
                    Content = "",
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall { Id = "call_1", Name = "read_file", ArgumentsJson = "{\"path\":\"/tmp/test.txt\"}" }
                    }
                },
                new Message { Role = Role.Tool, Content = "hello world", ToolCallId = "call_1" },
            }
        };

        await strategy.CompleteAsync(request);

        Assert.NotNull(handler.CapturedBody);
        using var doc = JsonDocument.Parse(handler.CapturedBody);
        var input = doc.RootElement.GetProperty("input");

        // Should have: user message, function_call, function_call_output
        Assert.Equal(3, input.GetArrayLength());

        // First item: user message
        Assert.Equal("user", input[0].GetProperty("role").GetString());

        // Second item: function_call
        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.Equal("call_1", input[1].GetProperty("call_id").GetString());
        Assert.Equal("read_file", input[1].GetProperty("name").GetString());

        // Third item: function_call_output
        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("call_1", input[2].GetProperty("call_id").GetString());
        Assert.Equal("hello world", input[2].GetProperty("output").GetString());
    }

    #endregion

    #region IsAvailableAsync Tests

    [Fact]
    public async Task IsAvailableAsync_SuccessfulResponse_ReturnsTrue()
    {
        var handler = new MockHttpHandler(HttpStatusCode.OK, "{}");
        var strategy = CreateStrategy(handler);

        var result = await strategy.IsAvailableAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task IsAvailableAsync_ErrorResponse_ReturnsFalse()
    {
        var handler = new MockHttpHandler(HttpStatusCode.Unauthorized, "{\"error\":\"invalid key\"}");
        var strategy = CreateStrategy(handler);

        var result = await strategy.IsAvailableAsync();
        Assert.False(result);
    }

    #endregion

    #region StreamCompleteAsync Tests

    [Fact]
    public async Task StreamCompleteAsync_Completed_PopulatesUsageFromNestedResponse()
    {
        // The Responses API nests usage under the "response" object of the
        // terminal response.completed event, not at the event root.
        var sse = BuildSse(
            ("response.output_text.delta", new { type = "response.output_text.delta", delta = "Hello" }),
            ("response.output_text.delta", new { type = "response.output_text.delta", delta = " world" }),
            ("response.completed", new
            {
                type = "response.completed",
                response = new
                {
                    id = "resp_123",
                    status = "completed",
                    usage = new { input_tokens = 42, output_tokens = 17, total_tokens = 59 }
                }
            }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var responses = await CollectStreamAsync(strategy);

        var terminal = responses.Last();
        Assert.True(terminal.IsComplete);
        Assert.Equal("stop", terminal.FinishReason);
        Assert.NotNull(terminal.Usage);
        Assert.Equal(42, terminal.Usage!.PromptTokens);
        Assert.Equal(17, terminal.Usage.CompletionTokens);
        Assert.Equal(59, terminal.Usage.TotalTokens);

        // Text deltas are still emitted along the way.
        var text = string.Concat(responses.Where(r => r.Delta?.Content != null).Select(r => r.Delta!.Content));
        Assert.Equal("Hello world", text);
    }

    [Fact]
    public async Task StreamCompleteAsync_Completed_TotalTokensFallsBackToSum()
    {
        // When total_tokens is absent, it should fall back to the input+output sum.
        var sse = BuildSse(
            ("response.completed", new
            {
                type = "response.completed",
                response = new
                {
                    id = "resp_nototal",
                    status = "completed",
                    usage = new { input_tokens = 8, output_tokens = 4 }
                }
            }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var terminal = (await CollectStreamAsync(strategy)).Last();

        Assert.NotNull(terminal.Usage);
        Assert.Equal(8, terminal.Usage!.PromptTokens);
        Assert.Equal(4, terminal.Usage.CompletionTokens);
        Assert.Equal(12, terminal.Usage.TotalTokens);
    }

    [Fact]
    public async Task StreamCompleteAsync_Failed_ReturnsErrorFinishReason()
    {
        var sse = BuildSse(
            ("response.failed", new
            {
                type = "response.failed",
                response = new
                {
                    id = "resp_fail",
                    status = "failed",
                    error = new { code = "server_error", message = "The model failed to generate a response." }
                }
            }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var terminal = (await CollectStreamAsync(strategy)).Last();

        Assert.True(terminal.IsComplete);
        Assert.Equal("error", terminal.FinishReason);
        Assert.NotNull(terminal.Delta);
        Assert.Equal("The model failed to generate a response.", terminal.Delta!.Content);
    }

    [Fact]
    public async Task StreamCompleteAsync_IncompleteMaxTokens_ReturnsLengthFinishReason()
    {
        var sse = BuildSse(
            ("response.incomplete", new
            {
                type = "response.incomplete",
                response = new
                {
                    id = "resp_inc",
                    status = "incomplete",
                    incomplete_details = new { reason = "max_output_tokens" },
                    usage = new { input_tokens = 100, output_tokens = 50, total_tokens = 150 }
                }
            }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var terminal = (await CollectStreamAsync(strategy)).Last();

        Assert.True(terminal.IsComplete);
        Assert.Equal("length", terminal.FinishReason);
        Assert.NotNull(terminal.Usage);
        Assert.Equal(100, terminal.Usage!.PromptTokens);
        Assert.Equal(50, terminal.Usage.CompletionTokens);
        Assert.Equal(150, terminal.Usage.TotalTokens);
    }

    [Fact]
    public async Task StreamCompleteAsync_IncompleteOtherReason_ReturnsIncompleteFinishReason()
    {
        var sse = BuildSse(
            ("response.incomplete", new
            {
                type = "response.incomplete",
                response = new
                {
                    id = "resp_inc2",
                    status = "incomplete",
                    incomplete_details = new { reason = "content_filter" }
                }
            }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var terminal = (await CollectStreamAsync(strategy)).Last();

        Assert.True(terminal.IsComplete);
        Assert.Equal("incomplete", terminal.FinishReason);
    }

    [Fact]
    public async Task StreamCompleteAsync_TopLevelErrorEvent_ReturnsErrorFinishReason()
    {
        var sse = BuildSse(
            ("error", new { type = "error", code = "rate_limit_exceeded", message = "Rate limit reached." }));

        var handler = new MockHttpHandler(HttpStatusCode.OK, sse);
        var strategy = CreateStrategy(handler);

        var terminal = (await CollectStreamAsync(strategy)).Last();

        Assert.True(terminal.IsComplete);
        Assert.Equal("error", terminal.FinishReason);
        Assert.NotNull(terminal.Delta);
        Assert.Equal("Rate limit reached.", terminal.Delta!.Content);
    }

    private static async Task<List<LlmStreamResponse>> CollectStreamAsync(ResponsesApiStrategy strategy)
    {
        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "Hi" } }
        };

        var results = new List<LlmStreamResponse>();
        await foreach (var chunk in strategy.StreamCompleteAsync(request))
        {
            results.Add(chunk);
        }
        return results;
    }

    /// <summary>
    /// Builds a Server-Sent Events body matching the Responses API wire format:
    /// each event is an "event: &lt;type&gt;" line followed by a "data: &lt;json&gt;" line.
    /// </summary>
    private static string BuildSse(params (string EventType, object Payload)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (eventType, payload) in events)
        {
            sb.Append("event: ").Append(eventType).Append('\n');
            sb.Append("data: ").Append(JsonSerializer.Serialize(payload)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Simple mock HTTP handler that returns a fixed response.
    /// </summary>
    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public MockHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>
    /// HTTP handler that throws a fixed exception. Used to verify the
    /// transport-failure path wraps lower-layer exceptions into
    /// InvalidOperationException per the provider contract.
    /// </summary>
    private class ThrowingHttpHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHttpHandler(Exception ex) { _ex = ex; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(_ex);
    }

    /// <summary>
    /// HTTP handler that captures the request body and returns a fixed response.
    /// </summary>
    private class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public string? CapturedBody { get; private set; }

        public CapturingHttpHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion
}
