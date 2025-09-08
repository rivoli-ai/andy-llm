namespace Andy.Llm.Models;

/// <summary>
/// Represents a message in a conversation
/// </summary>
public class Message
{
    /// <summary>
    /// The role of the message sender
    /// </summary>
    public required MessageRole Role { get; set; }

    /// <summary>
    /// The message parts (text, tool calls, etc.)
    /// </summary>
    public List<MessagePart> Parts { get; set; } = new();

    /// <summary>
    /// Helper to create a simple text message
    /// </summary>
    public static Message CreateText(MessageRole role, string content)
    {
        return new Message
        {
            Role = role,
            Parts = new List<MessagePart> { new TextPart { Text = content } }
        };
    }

    /// <summary>
    /// Gets the total character count of the message
    /// </summary>
    public int GetCharacterCount()
    {
        return Parts.Sum(p => p.GetCharacterCount());
    }
}

/// <summary>
/// Role of the message sender
/// </summary>
public enum MessageRole
{
    /// <summary>System message for instructions</summary>
    System,
    /// <summary>User message</summary>
    User,
    /// <summary>Assistant response</summary>
    Assistant,
    /// <summary>Tool/function response</summary>
    Tool
}

/// <summary>
/// Base class for message parts
/// </summary>
public abstract class MessagePart
{
    /// <summary>
    /// Gets the character count of this part
    /// </summary>
    public abstract int GetCharacterCount();
}

/// <summary>
/// Text content part
/// </summary>
public class TextPart : MessagePart
{
    /// <summary>
    /// The text content
    /// </summary>
    public required string Text { get; set; }

    /// <inheritdoc />
    public override int GetCharacterCount() => Text.Length;
}

/// <summary>
/// Tool call part from assistant
/// </summary>
public class ToolCallPart : MessagePart
{
    /// <summary>
    /// Name of the tool to call
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// Unique identifier for this call
    /// </summary>
    public required string CallId { get; set; }

    /// <summary>
    /// Arguments to pass to the tool
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();

    /// <inheritdoc />
    public override int GetCharacterCount()
    {
        var argJson = System.Text.Json.JsonSerializer.Serialize(Arguments);
        return ToolName.Length + CallId.Length + argJson.Length;
    }
}

/// <summary>
/// Tool response part
/// </summary>
public class ToolResponsePart : MessagePart
{
    /// <summary>
    /// Name of the tool that was called
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// The call ID this is responding to
    /// </summary>
    public required string CallId { get; set; }

    /// <summary>
    /// The response from the tool
    /// </summary>
    public object? Response { get; set; }

    /// <inheritdoc />
    public override int GetCharacterCount()
    {
        var responseStr = Response?.ToString() ?? string.Empty;
        return ToolName.Length + CallId.Length + responseStr.Length;
    }
}
