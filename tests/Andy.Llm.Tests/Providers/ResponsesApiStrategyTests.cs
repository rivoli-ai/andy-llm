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
    public async Task CompleteAsync_HttpError_ReturnsErrorResponse()
    {
        var handler = new MockHttpHandler(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"Model not found\"}}");
        var strategy = CreateStrategy(handler);

        var request = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "Hello" } }
        };

        var response = await strategy.CompleteAsync(request);

        Assert.Equal("error", response.FinishReason);
        Assert.Contains("NotFound", response.AssistantMessage.Content);
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
