using Xunit;
using Andy.Llm.Models;
using Andy.Context.Model;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for ConversationContext functionality
/// </summary>
public class ConversationContextTests
{
    [Fact]
    public void ConversationContext_ShouldInitializeEmpty()
    {
        // Arrange & Act
        var context = new ConversationContext();

        // Assert
        Assert.Empty(context.Messages);
        Assert.Empty(context.ComprehensiveHistory);
        Assert.Null(context.SystemInstruction);
        Assert.Empty(context.AvailableTools);
        Assert.Equal(50, context.MaxContextMessages);
        Assert.Equal(100000, context.MaxContextCharacters);
    }

    [Fact]
    public void AddUserMessage_ShouldAddMessage()
    {
        // Arrange
        var context = new ConversationContext();

        // Act
        context.AddUserMessage("Hello, world!");

        // Assert
        Assert.Single(context.Messages);
        var message = context.Messages[0];
        Assert.Equal(Role.User, message.Role);
        Assert.Equal("Hello, world!", message.Content);
    }

    [Fact]
    public void SystemInstruction_ShouldBeAddedToMessages()
    {
        // Arrange
        var context = new ConversationContext
        {
            SystemInstruction = "You are a helpful assistant"
        };

        // Act
        context.AddUserMessage("Hello");

        // Assert
        Assert.Equal(2, context.Messages.Count);
        Assert.Equal(Role.System, context.Messages[0].Role);
        Assert.Equal(Role.User, context.Messages[1].Role);
    }

    [Fact]
    public void CreateRequest_ShouldIncludeAllElements()
    {
        // Arrange
        var context = new ConversationContext();
        context.SystemInstruction = "Be helpful";
        context.AddUserMessage("What's 2+2?");
        context.AddAssistantMessage("2+2 equals 4");

        var tool = new ToolDeclaration
        {
            Name = "calculator",
            Description = "Performs calculations",
            Parameters = new Dictionary<string, object>()
        };
        context.AvailableTools.Add(tool);

        // Act
        var request = context.CreateRequest("gpt-4");

        // Assert
        Assert.Equal("gpt-4", request.Model);
        Assert.Equal("Be helpful", request.SystemPrompt);
        Assert.Equal(2, request.Messages.Count); // User and Assistant messages
        Assert.Single(request.Tools!);
        Assert.Equal("calculator", request.Tools![0].Name);
        // Also validate Functions alias maps to Tools
        Assert.Single(request.Functions!);
        Assert.Equal("calculator", request.Functions![0].Name);
    }

    [Fact]
    public void GetCharacterCount_ShouldCountAllText()
    {
        // Arrange
        var context = new ConversationContext();
        context.SystemInstruction = "System"; // 6 chars
        context.AddUserMessage("User");       // 4 chars
        context.AddAssistantMessage("Bot");   // 3 chars

        // Act
        var count = context.GetCharacterCount();

        // Assert
        Assert.Equal(13, count); // 6 + 4 + 3
    }

    [Fact]
    public void Clear_ShouldRemoveAllMessages()
    {
        // Arrange
        var context = new ConversationContext();
        context.SystemInstruction = "System";
        context.AddUserMessage("User");
        context.AddAssistantMessage("Assistant");

        // Act
        context.Clear();

        // Assert
        Assert.Empty(context.Messages);
        Assert.Empty(context.ComprehensiveHistory);
        Assert.Null(context.SystemInstruction);
    }

    [Fact]
    public void GetSummary_ShouldReturnFormattedSummary()
    {
        // Arrange
        var context = new ConversationContext();
        context.SystemInstruction = "Be helpful";
        context.AddUserMessage("Hello");
        context.AddAssistantMessage("Hi there!");

        // Act
        var summary = context.GetSummary();

        // Assert
        Assert.Contains("System: Be helpful", summary);
        Assert.Contains("User: Hello", summary);
        Assert.Contains("Assistant: Hi there!", summary);
    }

    [Fact]
    public void AddToolResponse_ShouldCreateToolMessage()
    {
        // Arrange
        var context = new ConversationContext();
        var response = new { temperature = 72, condition = "sunny" };

        // Act
        context.AddToolResponse("weather", "call_123", response);

        // Assert
        Assert.Single(context.Messages);
        var message = context.Messages[0];
        Assert.Equal(Role.Tool, message.Role);
        Assert.NotNull(message.ToolResults);
        Assert.Single(message.ToolResults);
        Assert.Equal("weather", message.ToolResults[0].Name);
        Assert.Equal("call_123", message.ToolResults[0].CallId);
        Assert.NotNull(message.ToolResults[0].ResultJson);
    }

    [Fact]
    public void AddAssistantMessageWithToolCalls_ShouldCreateComplexMessage()
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
            },
            new FunctionCall
            {
                Id = "call_2",
                Name = "get_time",
                Arguments = new Dictionary<string, object?> { ["timezone"] = "EST" }
            }
        };

        // Act
        context.AddAssistantMessageWithToolCalls("I'll check that", functionCalls);

        // Assert
        Assert.Single(context.Messages);
        var message = context.Messages[0];
        Assert.Equal(Role.Assistant, message.Role);
        Assert.Equal("I'll check that", message.Content);
        Assert.NotNull(message.ToolCalls);
        Assert.Equal(2, message.ToolCalls.Count);

        Assert.Equal("get_weather", message.ToolCalls[0].Name);
        Assert.Equal("call_1", message.ToolCalls[0].Id);

        Assert.Equal("get_time", message.ToolCalls[1].Name);
        Assert.Equal("call_2", message.ToolCalls[1].Id);
    }

    [Fact]
    public void ContextPruning_ShouldLimitMessageCount()
    {
        // Arrange
        var context = new ConversationContext
        {
            MaxContextMessages = 3
        };

        // Act
        context.AddUserMessage("Message 1");
        context.AddAssistantMessage("Response 1");
        context.AddUserMessage("Message 2");
        context.AddAssistantMessage("Response 2");
        context.AddUserMessage("Message 3");

        // Assert
        Assert.True(context.Messages.Count <= 3);
        Assert.Equal(5, context.ComprehensiveHistory.Count); // All messages kept in history
    }
}
