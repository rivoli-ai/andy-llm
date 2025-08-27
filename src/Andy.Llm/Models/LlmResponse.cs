namespace Andy.Llm.Models;

/// <summary>
/// Represents a complete response from an LLM provider
/// </summary>
public class LlmResponse
{
    /// <summary>
    /// The text content of the response
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Function/tool calls made by the assistant
    /// </summary>
    public List<FunctionCall> FunctionCalls { get; set; } = new();

    /// <summary>
    /// The reason the response finished
    /// </summary>
    public string? FinishReason { get; set; }

    /// <summary>
    /// Total tokens used in the request
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// The model that was actually used
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Provider-specific metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Represents a streaming response chunk from an LLM provider
/// </summary>
public class LlmStreamResponse
{
    /// <summary>
    /// Incremental text content
    /// </summary>
    public string? TextDelta { get; set; }

    /// <summary>
    /// Function call information (if complete)
    /// </summary>
    public FunctionCall? FunctionCall { get; set; }

    /// <summary>
    /// Whether this is the final chunk
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    /// Error information if any
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Represents a function call from the LLM
/// </summary>
public class FunctionCall
{
    /// <summary>
    /// The function name to call
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Unique identifier for this function call
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Arguments to pass to the function
    /// </summary>
    public Dictionary<string, object?> Arguments { get; set; } = new();
}