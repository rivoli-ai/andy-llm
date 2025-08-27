namespace Andy.Llm.Models;

/// <summary>
/// Represents a request to an LLM provider
/// </summary>
public class LlmRequest
{
    /// <summary>
    /// The conversation messages
    /// </summary>
    public List<Message> Messages { get; set; } = new();

    /// <summary>
    /// Available tools/functions that can be called
    /// </summary>
    public List<ToolDeclaration>? Tools { get; set; }

    /// <summary>
    /// The model to use (provider-specific)
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Temperature for response generation (0.0 to 2.0)
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Maximum tokens to generate
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// System instruction/prompt
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Whether to stream the response
    /// </summary>
    public bool Stream { get; set; }
}