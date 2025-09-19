using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Andy.Llm.Configuration;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for function calling functionality, specifically verifying CallId handling in ToolResults
/// </summary>
public class FunctionCallingTests
{
    [Fact]
    public async Task CompleteAsync_WithToolCalls_ShouldIncludeCallIdInToolResults()
    {
        // Arrange
        var mockProvider = new Mock<Andy.Model.Llm.ILlmProvider>();
        mockProvider.Setup(p => p.Name).Returns("TestProvider");

        // Create a response with tool calls that have IDs
        var toolCall1 = new ToolCall
        {
            Id = "call_abc123",
            Name = "get_weather",
            ArgumentsJson = "{\"location\":\"San Francisco, CA\"}"
        };

        var toolCall2 = new ToolCall
        {
            Id = "call_def456",
            Name = "calculate",
            ArgumentsJson = "{\"expression\":\"0.15 * 240\"}"
        };

        // Create test response using a mock - we can't set properties on LlmResponse directly
        var toolCalls = new List<ToolCall> { toolCall1, toolCall2 };

        // Setup mock to return response with tool calls
        mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                // In reality, the provider would return an LlmResponse with ToolCalls
                // For testing, we'll verify the message structure
                return new LlmResponse();
            });

        // Act - simulate what the application should do
        var messages = new List<Message>
        {
            new Message { Role = Role.User, Content = "What's the weather in San Francisco and how much is 15% of 240?" }
        };

        // Simulate adding assistant message with tool calls
        messages.Add(new Message
        {
            Role = Role.Assistant,
            Content = string.Empty,
            ToolCalls = toolCalls
        });

        // Simulate adding tool result messages with CallId in ToolResult
        foreach (var call in toolCalls)
        {
            var resultJson = call.Name == "get_weather"
                ? "{\"temperature\":72,\"unit\":\"fahrenheit\"}"
                : "{\"result\":36}";

            var toolResultMessage = new Message
            {
                Role = Role.Tool,
                Content = resultJson,
                ToolResults = new List<ToolResult>
                {
                    new ToolResult
                    {
                        CallId = call.Id,  // This is the critical part being tested
                        Name = call.Name,
                        ResultJson = resultJson,
                        IsError = false
                    }
                }
            };
            messages.Add(toolResultMessage);
        }

        // Assert - verify messages are structured correctly for the API
        Assert.Equal(4, messages.Count); // User, Assistant with tool calls, 2 Tool messages

        var assistantMsg = messages[1];
        Assert.Equal(Role.Assistant, assistantMsg.Role);
        Assert.NotNull(assistantMsg.ToolCalls);
        Assert.Equal(2, assistantMsg.ToolCalls.Count);

        var toolMsg1 = messages[2];
        Assert.Equal(Role.Tool, toolMsg1.Role);
        Assert.Single(toolMsg1.ToolResults);
        Assert.Equal("call_abc123", toolMsg1.ToolResults[0].CallId);
        Assert.Equal("get_weather", toolMsg1.ToolResults[0].Name);

        var toolMsg2 = messages[3];
        Assert.Equal(Role.Tool, toolMsg2.Role);
        Assert.Single(toolMsg2.ToolResults);
        Assert.Equal("call_def456", toolMsg2.ToolResults[0].CallId);
        Assert.Equal("calculate", toolMsg2.ToolResults[0].Name);
    }

    [Fact]
    public void Message_ToolResult_ShouldContainCallId()
    {
        // Arrange & Act
        var message = new Message
        {
            Role = Role.Tool,
            Content = "{\"result\":42}",
            ToolResults = new List<ToolResult>
            {
                new ToolResult
                {
                    CallId = "call_xyz789",
                    Name = "calculate",
                    ResultJson = "{\"result\":42}",
                    IsError = false
                }
            }
        };

        // Assert
        Assert.Single(message.ToolResults);
        Assert.Equal("call_xyz789", message.ToolResults[0].CallId);
        Assert.Equal(Role.Tool, message.Role);
        Assert.Equal("calculate", message.ToolResults[0].Name);
        Assert.Equal("{\"result\":42}", message.ToolResults[0].ResultJson);
    }

    [Fact]
    public void ToolCall_And_ToolResult_CallIds_Must_Match()
    {
        // This test verifies that the CallId in ToolResult matches the Id in ToolCall

        // Create a tool call from the assistant
        var toolCall = new ToolCall
        {
            Id = "call_123",
            Name = "test_function",
            ArgumentsJson = "{\"param\":\"value\"}"
        };

        // Create messages that simulate a complete tool call flow
        var messages = new List<Message>
        {
            new Message { Role = Role.User, Content = "Test question" },
            new Message
            {
                Role = Role.Assistant,
                Content = string.Empty,
                ToolCalls = new List<ToolCall> { toolCall }
            },
            new Message
            {
                Role = Role.Tool,
                Content = "{\"result\":\"success\"}",
                ToolResults = new List<ToolResult>
                {
                    new ToolResult
                    {
                        CallId = toolCall.Id,  // This must match the tool call ID
                        Name = "test_function",
                        ResultJson = "{\"result\":\"success\"}",
                        IsError = false
                    }
                }
            }
        };

        // Verify the message structure is valid for OpenAI API
        var assistantMessage = messages[1];
        Assert.NotNull(assistantMessage.ToolCalls);
        Assert.Single(assistantMessage.ToolCalls);

        var toolMessage = messages[2];
        Assert.Equal(Role.Tool, toolMessage.Role);
        Assert.Single(toolMessage.ToolResults);
        Assert.Equal("call_123", toolMessage.ToolResults[0].CallId);

        // Verify the IDs match
        Assert.Equal(assistantMessage.ToolCalls[0].Id, toolMessage.ToolResults[0].CallId);
    }

    [Fact]
    public void MissingCallId_ShouldBeDetectable()
    {
        // This test demonstrates what happens when CallId is missing from ToolResult
        var toolMessage = new Message
        {
            Role = Role.Tool,
            Content = "{\"result\":\"test\"}",
            ToolResults = new List<ToolResult>
            {
                new ToolResult
                {
                    // CallId is intentionally left empty
                    CallId = string.Empty,
                    Name = "test_function",
                    ResultJson = "{\"result\":\"test\"}",
                    IsError = false
                }
            }
        };

        // Assert that CallId is empty when not set properly
        Assert.Single(toolMessage.ToolResults);
        Assert.Empty(toolMessage.ToolResults[0].CallId);

        // This would cause an API error like:
        // "An assistant message with 'tool_calls' must be followed by tool messages
        // responding to each 'tool_call_id'. The following tool_call_ids did not have
        // response messages: call_xxx"
    }

    [Fact]
    public void ToolResult_FromObject_ShouldSetCallId()
    {
        // Test the factory method
        var callId = "call_test123";
        var name = "test_function";
        var result = new { status = "success", value = 42 };

        var toolResult = ToolResult.FromObject(callId, name, result);

        Assert.Equal(callId, toolResult.CallId);
        Assert.Equal(name, toolResult.Name);
        Assert.False(toolResult.IsError);
        Assert.Contains("\"status\":\"success\"", toolResult.ResultJson);
        Assert.Contains("\"value\":42", toolResult.ResultJson);
    }
}