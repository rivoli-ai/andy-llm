using Andy.Llm.Models;
using System.Collections.Generic;
using Xunit;

namespace Andy.Llm.Tests.Models;

public class MessageTests
{
    [Fact]
    public void Message_CanBeCreatedWithoutRequiredKeyword()
    {
        // Arrange & Act - Should not throw when creating without required properties
        var message = new Message
        {
            Role = MessageRole.User
        };

        // Assert
        Assert.Equal(MessageRole.User, message.Role);
        Assert.NotNull(message.Parts);
        Assert.Empty(message.Parts);
    }

    [Fact]
    public void Message_DefaultRoleIsSystem()
    {
        // Arrange & Act
        var message = new Message();

        // Assert
        Assert.Equal(MessageRole.System, message.Role);
    }

    [Fact]
    public void TextPart_HandlesNullTextGracefully()
    {
        // Arrange
        var textPart = new TextPart { Text = null! };

        // Act
        var charCount = textPart.GetCharacterCount();

        // Assert
        Assert.Equal(0, charCount);
    }

    [Fact]
    public void TextPart_HasEmptyStringDefault()
    {
        // Arrange & Act
        var textPart = new TextPart();

        // Assert
        Assert.Equal(string.Empty, textPart.Text);
        Assert.Equal(0, textPart.GetCharacterCount());
    }

    [Fact]
    public void TextPart_ReturnsCorrectCharacterCount()
    {
        // Arrange
        var textPart = new TextPart { Text = "Hello, World!" };

        // Act
        var charCount = textPart.GetCharacterCount();

        // Assert
        Assert.Equal(13, charCount);
    }

    [Fact]
    public void ToolCallPart_HasDefaultValues()
    {
        // Arrange & Act
        var toolCall = new ToolCallPart();

        // Assert
        Assert.Equal(string.Empty, toolCall.ToolName);
        Assert.Equal(string.Empty, toolCall.CallId);
        Assert.NotNull(toolCall.Arguments);
        Assert.Empty(toolCall.Arguments);
    }

    [Fact]
    public void ToolCallPart_CanBeCreatedWithoutRequiredProperties()
    {
        // Arrange & Act - Should not throw
        var toolCall = new ToolCallPart
        {
            ToolName = "calculator",
            CallId = "call-123"
        };

        // Assert
        Assert.Equal("calculator", toolCall.ToolName);
        Assert.Equal("call-123", toolCall.CallId);
    }

    [Fact]
    public void ToolResponsePart_HasDefaultValues()
    {
        // Arrange & Act
        var toolResponse = new ToolResponsePart();

        // Assert
        Assert.Equal(string.Empty, toolResponse.ToolName);
        Assert.Equal(string.Empty, toolResponse.CallId);
        Assert.Null(toolResponse.Response);
    }

    [Fact]
    public void ToolResponsePart_CanBeCreatedWithoutRequiredProperties()
    {
        // Arrange & Act - Should not throw
        var toolResponse = new ToolResponsePart
        {
            ToolName = "calculator",
            CallId = "call-123",
            Response = "42"
        };

        // Assert
        Assert.Equal("calculator", toolResponse.ToolName);
        Assert.Equal("call-123", toolResponse.CallId);
        Assert.Equal("42", toolResponse.Response);
    }

    [Fact]
    public void Message_CanAddMultipleParts()
    {
        // Arrange
        var message = new Message
        {
            Role = MessageRole.Assistant
        };

        // Act
        message.Parts.Add(new TextPart { Text = "Here's the result: " });
        message.Parts.Add(new ToolCallPart 
        { 
            ToolName = "calculator",
            CallId = "calc-1",
            Arguments = new Dictionary<string, object?>
            {
                ["operation"] = "add",
                ["a"] = 2,
                ["b"] = 3
            }
        });

        // Assert
        Assert.Equal(2, message.Parts.Count);
        Assert.IsType<TextPart>(message.Parts[0]);
        Assert.IsType<ToolCallPart>(message.Parts[1]);
    }

    [Fact]
    public void ToolResponsePart_GetCharacterCountHandlesNullResponse()
    {
        // Arrange
        var toolResponse = new ToolResponsePart
        {
            ToolName = "test",
            CallId = "123",
            Response = null
        };

        // Act
        var charCount = toolResponse.GetCharacterCount();

        // Assert
        // "test" (4) + "123" (3) + empty string (0) = 7
        Assert.Equal(7, charCount);
    }

    [Fact]
    public void ToolResponsePart_GetCharacterCountReturnsResponseLength()
    {
        // Arrange
        var toolResponse = new ToolResponsePart
        {
            Response = "This is a test response"
        };

        // Act
        var charCount = toolResponse.GetCharacterCount();

        // Assert
        Assert.Equal(23, charCount);
    }

    [Fact]
    public void ToolCallPart_GetCharacterCountIncludesAllFields()
    {
        // Arrange
        var toolCall = new ToolCallPart
        {
            ToolName = "test",
            CallId = "123",
            Arguments = new Dictionary<string, object?>
            {
                ["key"] = "value"
            }
        };

        // Act
        var charCount = toolCall.GetCharacterCount();

        // Assert
        // "test" (4) + "123" (3) + serialized args length
        Assert.True(charCount > 7); // At minimum tool name + call id
    }

    [Fact]
    public void ToolCallPart_GetCharacterCountHandlesEmptyArguments()
    {
        // Arrange
        var toolCall = new ToolCallPart
        {
            ToolName = "test",
            CallId = "123",
            Arguments = new Dictionary<string, object?>()
        };

        // Act
        var charCount = toolCall.GetCharacterCount();

        // Assert
        // "test" (4) + "123" (3) + "{}" (2)
        Assert.Equal(9, charCount);
    }
}