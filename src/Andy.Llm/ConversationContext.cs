using Andy.Llm.Models;

namespace Andy.Llm;

/// <summary>
/// Manages conversation context for LLM interactions
/// </summary>
public class ConversationContext
{
    private readonly List<Message> _messages = new();
    private readonly List<Message> _comprehensiveHistory = new();
    private string? _systemInstruction;

    /// <summary>
    /// Current conversation messages
    /// </summary>
    public IReadOnlyList<Message> Messages => _messages.AsReadOnly();

    /// <summary>
    /// Complete conversation history including all interactions
    /// </summary>
    public IReadOnlyList<Message> ComprehensiveHistory => _comprehensiveHistory.AsReadOnly();

    /// <summary>
    /// System instruction/prompt
    /// </summary>
    public string? SystemInstruction
    {
        get => _systemInstruction;
        set
        {
            _systemInstruction = value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Update or add system message at the beginning
                var systemMessage = _messages.FirstOrDefault(m => m.Role == MessageRole.System);
                if (systemMessage != null)
                {
                    var textPart = systemMessage.Parts.OfType<TextPart>().FirstOrDefault();
                    if (textPart != null)
                    {
                        systemMessage.Parts[systemMessage.Parts.IndexOf(textPart)] = new TextPart { Text = value };
                    }
                }
                else
                {
                    _messages.Insert(0, Message.CreateText(MessageRole.System, value));
                }
            }
        }
    }

    /// <summary>
    /// Available tools for the conversation
    /// </summary>
    public List<ToolDeclaration> AvailableTools { get; set; } = new();

    /// <summary>
    /// Maximum number of messages to keep in context
    /// </summary>
    public int MaxContextMessages { get; set; } = 50;

    /// <summary>
    /// Maximum character count for context
    /// </summary>
    public int MaxContextCharacters { get; set; } = 100000;

    /// <summary>
    /// Adds a user message to the conversation
    /// </summary>
    public void AddUserMessage(string content)
    {
        var message = Message.CreateText(MessageRole.User, content);
        _messages.Add(message);
        _comprehensiveHistory.Add(message);
        TrimContext();
    }

    /// <summary>
    /// Adds an assistant message to the conversation
    /// </summary>
    public void AddAssistantMessage(string content)
    {
        var message = Message.CreateText(MessageRole.Assistant, content);
        _messages.Add(message);
        _comprehensiveHistory.Add(message);
        TrimContext();
    }

    /// <summary>
    /// Adds an assistant message with tool calls
    /// </summary>
    public void AddAssistantMessageWithToolCalls(string? content, List<FunctionCall> functionCalls)
    {
        var message = new Message
        {
            Role = MessageRole.Assistant,
            Parts = new List<MessagePart>()
        };

        // Add text content if present
        if (!string.IsNullOrEmpty(content))
        {
            message.Parts.Add(new TextPart { Text = content });
        }

        // Add tool calls
        foreach (var functionCall in functionCalls)
        {
            message.Parts.Add(new ToolCallPart
            {
                ToolName = functionCall.Name,
                CallId = functionCall.Id,
                Arguments = functionCall.Arguments
            });
        }

        _messages.Add(message);
        _comprehensiveHistory.Add(message);
        TrimContext();
    }

    /// <summary>
    /// Adds a tool response to the conversation
    /// </summary>
    public void AddToolResponse(string toolName, string callId, object response)
    {
        var message = new Message
        {
            Role = MessageRole.Tool,
            Parts = new List<MessagePart>
            {
                new ToolResponsePart
                {
                    ToolName = toolName,
                    CallId = callId,
                    Response = response
                }
            }
        };

        _messages.Add(message);
        _comprehensiveHistory.Add(message);
        TrimContext();
    }

    /// <summary>
    /// Gets the total character count of the current context
    /// </summary>
    public int GetCharacterCount()
    {
        return _messages.Sum(m => m.GetCharacterCount());
    }

    /// <summary>
    /// Clears the conversation context
    /// </summary>
    public void Clear()
    {
        _messages.Clear();
        _comprehensiveHistory.Clear();
        _systemInstruction = null;
    }

    /// <summary>
    /// Creates an LLM request from the current context
    /// </summary>
    public LlmRequest CreateRequest(string? model = null)
    {
        // Filter out system messages if SystemPrompt is being used
        var messagesToInclude = !string.IsNullOrEmpty(SystemInstruction)
            ? _messages.Where(m => m.Role != MessageRole.System).ToList()
            : new List<Message>(_messages);

        return new LlmRequest
        {
            Messages = messagesToInclude,
            Tools = AvailableTools.Any() ? AvailableTools : null,
            Model = model,
            SystemPrompt = SystemInstruction
        };
    }

    /// <summary>
    /// Trims the context to stay within limits
    /// </summary>
    private void TrimContext()
    {
        // Keep system message if present
        var systemMessage = _messages.FirstOrDefault(m => m.Role == MessageRole.System);
        var messagesToTrim = systemMessage != null
            ? _messages.Skip(1).ToList()
            : _messages.ToList();

        // Trim by message count
        while (_messages.Count > MaxContextMessages && messagesToTrim.Count > 2)
        {
            _messages.Remove(messagesToTrim[0]);
            messagesToTrim.RemoveAt(0);
        }

        // Trim by character count
        while (GetCharacterCount() > MaxContextCharacters && messagesToTrim.Count > 2)
        {
            _messages.Remove(messagesToTrim[0]);
            messagesToTrim.RemoveAt(0);
        }
    }

    /// <summary>
    /// Gets a summary of the conversation
    /// </summary>
    public string GetSummary()
    {
        var lines = new List<string>();

        foreach (var message in _messages)
        {
            var role = message.Role.ToString();
            var content = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));

            if (!string.IsNullOrEmpty(content))
            {
                lines.Add($"{role}: {content}");
            }
        }

        return string.Join("\n", lines);
    }
}
