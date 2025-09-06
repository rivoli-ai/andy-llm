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

    /// <summary>
    /// Response format specification for structured outputs
    /// </summary>
    public ResponseFormat? ResponseFormat { get; set; }

    /// <summary>
    /// JSON Schema for structured output validation
    /// </summary>
    public string? JsonSchema { get; set; }

    /// <summary>
    /// Tool choice strategy (auto, none, required, or specific tool name)
    /// </summary>
    public ToolChoice? ToolChoice { get; set; }

    /// <summary>
    /// Whether to enable strict mode for structured outputs (provider-specific)
    /// </summary>
    public bool? StrictMode { get; set; }

    /// <summary>
    /// Additional provider-specific options
    /// </summary>
    public Dictionary<string, object>? ProviderOptions { get; set; }
}

/// <summary>
/// Specifies the format of the response
/// </summary>
 public enum ResponseFormat
{
    /// <summary>
    /// Standard text response
    /// </summary>
    Text,
    
    /// <summary>
    /// JSON-only response
    /// </summary>
    JsonObject,
    
    /// <summary>
    /// JSON response conforming to provided schema
    /// </summary>
    JsonSchema,
    
    /// <summary>
    /// XML response
    /// </summary>
    Xml,
    
    /// <summary>
    /// Tool/function calls only
    /// </summary>
    ToolCalls
}

/// <summary>
/// Tool choice strategy for function calling
/// </summary>
public class ToolChoice
{
    /// <summary>
    /// Strategy type: auto, none, required, or specific
    /// </summary>
    public string Type { get; set; } = "auto";
    
    /// <summary>
    /// Specific tool name if Type is "specific"
    /// </summary>
    public string? ToolName { get; set; }
    
    /// <summary>
    /// Creates a tool choice that lets the model decide
    /// </summary>
    public static ToolChoice Auto => new() { Type = "auto" };
    
    /// <summary>
    /// Creates a tool choice that prevents tool use
    /// </summary>
    public static ToolChoice None => new() { Type = "none" };
    
    /// <summary>
    /// Creates a tool choice that requires tool use
    /// </summary>
    public static ToolChoice Required => new() { Type = "required" };
    
    /// <summary>
    /// Creates a tool choice for a specific tool
    /// </summary>
    public static ToolChoice Specific(string toolName) => new() { Type = "specific", ToolName = toolName };
}