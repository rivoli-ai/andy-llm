using System;
using System.Collections.Generic;
using System.Linq;

namespace Andy.Llm.Parsing;

/// <summary>
/// Represents a structured response from an LLM provider (OpenAI, Anthropic, etc.)
/// This is used when the provider returns tool calls as separate structured objects
/// rather than embedded in text content.
/// </summary>
public class StructuredLlmResponse
{
    /// <summary>
    /// Text content from the response
    /// </summary>
    public string? TextContent { get; set; }
    
    /// <summary>
    /// Structured tool calls from the provider
    /// </summary>
    public List<StructuredToolCall> ToolCalls { get; set; } = new();
    
    /// <summary>
    /// Tool results from previous calls (for conversation history)
    /// </summary>
    public List<StructuredToolResult> ToolResults { get; set; } = new();
    
    /// <summary>
    /// Response metadata
    /// </summary>
    public StructuredResponseMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Represents a tool call from a structured provider response
/// </summary>
public class StructuredToolCall
{
    /// <summary>
    /// Unique identifier for the tool call
    /// </summary>
    public string Id { get; set; } = "";
    
    /// <summary>
    /// Name of the tool/function to call
    /// </summary>
    public string Name { get; set; } = "";
    
    /// <summary>
    /// Arguments as a raw JSON string (safer than pre-parsed objects)
    /// </summary>
    public string ArgumentsJson { get; set; } = "";
    
    /// <summary>
    /// Arguments as a parsed dictionary (may be null if parsing failed)
    /// </summary>
    public Dictionary<string, object?>? Arguments { get; set; }
    
    /// <summary>
    /// Error that occurred during argument parsing, if any
    /// </summary>
    public Exception? ParseError { get; set; }
}

/// <summary>
/// Represents a tool execution result from a structured provider
/// </summary>
public class StructuredToolResult
{
    /// <summary>
    /// ID of the tool call this is a result for
    /// </summary>
    public string CallId { get; set; } = "";
    
    /// <summary>
    /// Name of the tool that was called
    /// </summary>
    public string ToolName { get; set; } = "";
    
    /// <summary>
    /// Result of the tool execution
    /// </summary>
    public object? Result { get; set; }
    
    /// <summary>
    /// Whether the tool execution was successful
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// Error message if the tool execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Exception that occurred during tool execution, if any
    /// </summary>
    public Exception? ExecutionError { get; set; }
}

/// <summary>
/// Metadata about a structured response
/// </summary>
public class StructuredResponseMetadata
{
    /// <summary>
    /// Provider that generated the response (OpenAI, Anthropic, etc.)
    /// </summary>
    public string Provider { get; set; } = "";
    
    /// <summary>
    /// Model name that generated the response
    /// </summary>
    public string Model { get; set; } = "";
    
    /// <summary>
    /// Timestamp when the response was generated
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Reason why the response finished (completed, tool_calls, length, etc.)
    /// </summary>
    public string? FinishReason { get; set; }
    
    /// <summary>
    /// Token usage information
    /// </summary>
    public TokenUsage? Usage { get; set; }
    
    /// <summary>
    /// Whether this response contains tool calls that need to be executed
    /// </summary>
    public bool HasPendingToolCalls => ToolCalls?.Any() == true;
    
    /// <summary>
    /// List of tool calls that need to be executed
    /// </summary>
    public List<StructuredToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// Token usage information
/// </summary>
public class TokenUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>
/// Interface for creating structured responses from provider-specific formats
/// </summary>
public interface IStructuredResponseFactory
{
    /// <summary>
    /// Creates a structured response from OpenAI-style tool calls
    /// </summary>
    StructuredLlmResponse CreateFromOpenAI(object openAIResponse);
    
    /// <summary>
    /// Creates a structured response from Anthropic-style tool calls
    /// </summary>
    StructuredLlmResponse CreateFromAnthropic(object anthropicResponse);
    
    /// <summary>
    /// Creates a structured response from a generic provider response
    /// </summary>
    StructuredLlmResponse CreateFromGeneric(string textContent, object? toolCallsData);
}

/// <summary>
/// Safe argument parsing utilities inspired by Microsoft.Extensions.AI
/// </summary>
public static class StructuredArgumentParser
{
    /// <summary>
    /// Safely parses JSON arguments into a dictionary, capturing any exceptions
    /// </summary>
    public static (Dictionary<string, object?>? arguments, Exception? error) SafeParseArguments(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (new Dictionary<string, object?>(), null);
        }
        
        try
        {
            var arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            return (arguments, null);
        }
        catch (Exception ex)
        {
            return (null, new InvalidOperationException($"Error parsing tool call arguments: {ex.Message}", ex));
        }
    }
    
    /// <summary>
    /// Creates a StructuredToolCall with safe argument parsing
    /// </summary>
    public static StructuredToolCall CreateToolCall(string id, string name, string argumentsJson)
    {
        var (arguments, error) = SafeParseArguments(argumentsJson);
        
        return new StructuredToolCall
        {
            Id = id,
            Name = name,
            ArgumentsJson = argumentsJson,
            Arguments = arguments,
            ParseError = error
        };
    }
}