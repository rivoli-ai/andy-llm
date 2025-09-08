using Xunit;
using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Abstractions;
using Moq;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for function calling functionality
/// </summary>
public class FunctionCallingTests
{
    private readonly Mock<ILlmProvider> _mockProvider;
    private readonly Mock<ILogger<LlmClient>> _mockLogger;
    private readonly LlmClient _client;

    public FunctionCallingTests()
    {
        _mockProvider = new Mock<ILlmProvider>();
        _mockProvider.Setup(p => p.Name).Returns("MockProvider");
        _mockLogger = new Mock<ILogger<LlmClient>>();
        _client = new LlmClient(_mockProvider.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task CompleteAsync_WithTools_ShouldReturnFunctionCalls()
    {
        // Arrange
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "What's the weather in New York?")
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "get_weather",
                    Description = "Get weather for a location",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new { type = "string" }
                        }
                    }
                }
            }
        };

        var functionCall = new FunctionCall
        {
            Id = "call_123",
            Name = "get_weather",
            Arguments = new Dictionary<string, object?> { ["location"] = "New York" }
        };

        var expectedResponse = new LlmResponse
        {
            Content = "", // Function calls might have empty content
            FunctionCalls = new List<FunctionCall> { functionCall },
            FinishReason = "tool_calls"
        };

        _mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _client.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.FunctionCalls);
        Assert.Single(response.FunctionCalls);
        Assert.Equal("get_weather", response.FunctionCalls[0].Name);
        Assert.Equal("New York", response.FunctionCalls[0].Arguments["location"]);
        Assert.Null(response.FunctionCalls[0].ArgumentsJson);
    }

    [Fact]
    public void ConversationContext_WithTools_ShouldIncludeInRequest()
    {
        // Arrange
        var context = new ConversationContext();
        var tool = new ToolDeclaration
        {
            Name = "calculate",
            Description = "Perform calculations",
            Parameters = new Dictionary<string, object>()
        };
        context.AvailableTools.Add(tool);
        context.AddUserMessage("What is 2+2?");

        // Act
        var request = context.CreateRequest();

        // Assert
        Assert.NotNull(request.Tools);
        Assert.Single(request.Tools);
        Assert.Equal("calculate", request.Tools[0].Name);
    }

    [Fact]
    public void ConversationContext_AddToolResponse_ShouldCreateCorrectMessage()
    {
        // Arrange
        var context = new ConversationContext();
        var weatherData = new { temperature = 72, condition = "sunny" };

        // Act
        context.AddToolResponse("get_weather", "call_123", weatherData);

        // Assert
        Assert.Single(context.Messages);
        var message = context.Messages[0];
        Assert.Equal(MessageRole.Tool, message.Role);
        Assert.Single(message.Parts);

        var toolPart = message.Parts[0] as ToolResponsePart;
        Assert.NotNull(toolPart);
        Assert.Equal("get_weather", toolPart.ToolName);
        Assert.Equal("call_123", toolPart.CallId);
        Assert.NotNull(toolPart.Response);
    }

    [Fact]
    public void ConversationContext_AddAssistantWithToolCalls_ShouldWork()
    {
        // Arrange
        var context = new ConversationContext();
        var functionCalls = new List<FunctionCall>
        {
            new FunctionCall
            {
                Id = "call_1",
                Name = "get_weather",
                Arguments = new Dictionary<string, object?> { ["location"] = "NYC" }
            }
        };

        // Act
        context.AddAssistantMessageWithToolCalls("Let me check the weather", functionCalls);

        // Assert
        Assert.Single(context.Messages);
        var message = context.Messages[0];
        Assert.Equal(MessageRole.Assistant, message.Role);
        Assert.Equal(2, message.Parts.Count); // Text + tool call

        var textPart = message.Parts[0] as TextPart;
        Assert.NotNull(textPart);
        Assert.Equal("Let me check the weather", textPart.Text);

        var toolCallPart = message.Parts[1] as ToolCallPart;
        Assert.NotNull(toolCallPart);
        Assert.Equal("get_weather", toolCallPart.ToolName);
        Assert.Equal("call_1", toolCallPart.CallId);
    }

    [Fact]
    public async Task CompleteAsync_WithEmptyContent_ShouldNotThrow()
    {
        // Arrange - Response with function call but no content
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Calculate 34*12")
            }
        };

        var functionCall = new FunctionCall
        {
            Id = "call_456",
            Name = "calculate",
            Arguments = new Dictionary<string, object?> { ["expression"] = "34*12" }
        };

        var expectedResponse = new LlmResponse
        {
            Content = "", // Empty content when only function calling
            FunctionCalls = new List<FunctionCall> { functionCall },
            FinishReason = "tool_calls"
        };

        _mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var response = await _client.CompleteAsync(request);

        // Assert - Should not throw and should have function calls
        Assert.NotNull(response);
        Assert.Empty(response.Content);
        Assert.NotNull(response.FunctionCalls);
        Assert.Single(response.FunctionCalls);
        Assert.Equal("calculate", response.FunctionCalls[0].Name);
    }
}
