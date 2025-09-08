using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Andy.Llm.Parsing.Ast;

namespace Andy.Llm.Parsing;

/// <summary>
/// Hybrid LLM response parser that handles both structured API responses (OpenAI, Anthropic)
/// and text-based responses (Qwen, raw text models) using existing parsers as fallback.
/// 
/// Inspired by Microsoft.Extensions.AI patterns but without the dependency.
/// </summary>
public class HybridLlmParser : ILlmResponseParser
{
    private readonly ILlmResponseParser _textParser;
    private readonly ILogger<HybridLlmParser>? _logger;
    private readonly IStructuredResponseFactory _structuredFactory;

    public HybridLlmParser(
        ILlmResponseParser textParser,
        IStructuredResponseFactory structuredFactory,
        ILogger<HybridLlmParser>? logger = null)
    {
        _textParser = textParser ?? throw new ArgumentNullException(nameof(textParser));
        _structuredFactory = structuredFactory ?? throw new ArgumentNullException(nameof(structuredFactory));
        _logger = logger;
    }

    /// <summary>
    /// Parses either structured or text-based LLM responses
    /// </summary>
    public ResponseNode Parse(string input, ParserContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return CreateEmptyResponse();
        }

        try
        {
            // Try to determine if this is a structured response or raw text
            var structuredResponse = TryParseAsStructuredResponse(input);

            if (structuredResponse != null)
            {
                _logger?.LogDebug("Processing structured response with {ToolCallCount} tool calls",
                    structuredResponse.ToolCalls.Count);
                return ParseStructuredResponseSync(structuredResponse);
            }
            else
            {
                _logger?.LogDebug("Processing text-based response with fallback parser");
                return _textParser.Parse(input, context);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing LLM response, falling back to text parser");

            // Always fallback to text parsing if structured parsing fails
            try
            {
                return _textParser.Parse(input, context);
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogError(fallbackEx, "Fallback text parsing also failed");

                // Return a basic error node if everything fails
                return CreateErrorResponse(ex, input);
            }
        }
    }

    /// <summary>
    /// Parse streaming response chunks into an AST
    /// </summary>
    public async Task<ResponseNode> ParseStreamingAsync(
        IAsyncEnumerable<string> chunks,
        ParserContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Buffer to accumulate chunks for structured response detection
        var buffer = new System.Text.StringBuilder();
        var isStructuredFormat = false;
        var structuredDetectionComplete = false;
        var initialChunks = new List<string>();

        try
        {
            await foreach (var chunk in chunks.WithCancellation(cancellationToken))
            {
                if (!structuredDetectionComplete)
                {
                    buffer.Append(chunk);
                    initialChunks.Add(chunk);

                    // Check if we have enough data to determine format
                    var currentBuffer = buffer.ToString();
                    if (currentBuffer.Length > 50 || currentBuffer.Contains("}") || currentBuffer.Contains("\n\n") ||
                        (currentBuffer.Length > 20 && !currentBuffer.TrimStart().StartsWith("{") && !currentBuffer.TrimStart().StartsWith("[")))
                    {
                        isStructuredFormat = IsStructuredResponseFormat(currentBuffer);
                        structuredDetectionComplete = true;

                        if (isStructuredFormat)
                        {
                            // Continue buffering for structured response
                            _logger?.LogDebug("Detected structured format in stream, buffering complete response");
                        }
                        else
                        {
                            // Switch to text streaming
                            _logger?.LogDebug("Detected text format in stream, delegating to text parser");

                            // Create a new enumerable that includes buffered chunks
                            var combinedChunks = CreateCombinedChunks(initialChunks, chunks, cancellationToken);
                            return await _textParser.ParseStreamingAsync(combinedChunks, context, cancellationToken);
                        }
                    }
                }
                else if (isStructuredFormat)
                {
                    buffer.Append(chunk);
                }
            }

            // If we accumulated a structured response, parse it
            if (isStructuredFormat || (structuredDetectionComplete && buffer.Length > 0))
            {
                var completeResponse = buffer.ToString();
                return Parse(completeResponse, context);
            }

            // Fallback to text parser for any remaining content
            var finalContent = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(finalContent))
            {
                return _textParser.Parse(finalContent, context);
            }

            return CreateEmptyResponse();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in streaming parse");

            // Try to parse what we have
            var partialContent = buffer.ToString();
            if (!string.IsNullOrWhiteSpace(partialContent))
            {
                try
                {
                    return _textParser.Parse(partialContent, context);
                }
                catch
                {
                    return CreateErrorResponse(ex, partialContent);
                }
            }

            return CreateErrorResponse(ex, "");
        }
    }

    /// <summary>
    /// Creates a combined async enumerable from buffered chunks and remaining stream
    /// </summary>
    private async IAsyncEnumerable<string> CreateCombinedChunks(
        List<string> bufferedChunks,
        IAsyncEnumerable<string> remainingChunks,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Yield buffered chunks first
        foreach (var chunk in bufferedChunks)
        {
            yield return chunk;
        }

        // Then yield remaining chunks
        await foreach (var chunk in remainingChunks.WithCancellation(cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Validate the AST for completeness and correctness
    /// </summary>
    public ValidationResult Validate(ResponseNode ast)
    {
        var issues = new List<ValidationIssue>();

        // Validate tool calls have unique IDs
        var toolCalls = ast.Children.OfType<ToolCallNode>().ToList();
        var duplicateIds = toolCalls.GroupBy(tc => tc.CallId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicateId in duplicateIds)
        {
            issues.Add(new ValidationIssue
            {
                Message = $"Duplicate tool call ID: {duplicateId}",
                Severity = ValidationSeverity.Error
            });
        }

        // Validate tool results have corresponding tool calls
        var toolResults = ast.Children.OfType<ToolResultNode>().ToList();
        var callIds = new HashSet<string>(toolCalls.Select(tc => tc.CallId));

        foreach (var result in toolResults)
        {
            if (!callIds.Contains(result.CallId))
            {
                issues.Add(new ValidationIssue
                {
                    Message = $"Tool result without corresponding call: {result.CallId}",
                    Severity = ValidationSeverity.Warning,
                    Node = result
                });
            }
        }

        // Check for parsing errors in tool calls
        foreach (var toolCall in toolCalls)
        {
            if (toolCall.ParseError != null)
            {
                issues.Add(new ValidationIssue
                {
                    Message = $"Tool call parsing error: {toolCall.ParseError.Message}",
                    Severity = ValidationSeverity.Error,
                    Node = toolCall
                });
            }
        }

        var hasErrors = issues.Any(i => i.Severity == ValidationSeverity.Error);
        return new ValidationResult
        {
            IsValid = !hasErrors,
            Issues = issues
        };
    }

    /// <summary>
    /// Get parser capabilities and supported features
    /// </summary>
    public ParserCapabilities GetCapabilities()
    {
        // Combine capabilities of structured and text parsing
        var textCapabilities = _textParser.GetCapabilities();

        return new ParserCapabilities
        {
            SupportsStreaming = textCapabilities.SupportsStreaming,
            SupportsToolCalls = true, // Enhanced with structured support
            SupportsCodeBlocks = textCapabilities.SupportsCodeBlocks,
            SupportsMarkdown = textCapabilities.SupportsMarkdown,
            SupportsFileReferences = textCapabilities.SupportsFileReferences,
            SupportsQuestions = textCapabilities.SupportsQuestions,
            SupportsThoughts = textCapabilities.SupportsThoughts,
            SupportedFormats = new List<string>(textCapabilities.SupportedFormats)
            {
                "structured-openai", "structured-anthropic", "structured-generic"
            }
        };
    }

    /// <summary>
    /// Attempts to parse input as a structured response
    /// Returns null if it appears to be raw text
    /// </summary>
    private StructuredLlmResponse? TryParseAsStructuredResponse(string input)
    {
        try
        {
            // Try to detect if this looks like structured JSON containing tool calls
            if (IsStructuredResponseFormat(input))
            {
                return ParseStructuredJson(input);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to parse as structured response, treating as text");
            return null;
        }
    }

    /// <summary>
    /// Determines if the input looks like a structured response rather than raw text
    /// </summary>
    private static bool IsStructuredResponseFormat(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();

        // Check for JSON object structure
        if (trimmed.StartsWith("{"))
        {
            // OpenAI patterns
            if (trimmed.Contains("\"tool_calls\"") ||
                trimmed.Contains("\"function_call\"") ||
                trimmed.Contains("\"choices\"") && trimmed.Contains("\"message\""))
            {
                return true;
            }

            // Anthropic patterns
            if (trimmed.Contains("\"content\"") && trimmed.Contains("\"type\"") &&
                (trimmed.Contains("\"tool_use\"") || trimmed.Contains("\"text\"")))
            {
                return true;
            }

            // Generic API response patterns
            if (trimmed.Contains("\"model\"") &&
                (trimmed.Contains("\"usage\"") || trimmed.Contains("\"finish_reason\"") ||
                 trimmed.Contains("\"stop_reason\"")))
            {
                return true;
            }

            // Streaming chunk patterns
            if (trimmed.Contains("\"delta\"") || trimmed.Contains("\"chunk\"") ||
                trimmed.Contains("event:") || trimmed.Contains("data:"))
            {
                return true;
            }
        }

        // Check for SSE (Server-Sent Events) format
        if (trimmed.StartsWith("data:") || trimmed.StartsWith("event:"))
        {
            return true;
        }

        // Check for JSONL format with structured objects
        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].Trim().StartsWith("{"))
        {
            var firstLine = lines[0].Trim();
            if (firstLine.Contains("\"tool") || firstLine.Contains("\"function") ||
                firstLine.Contains("\"type\"") || firstLine.Contains("\"role\""))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses structured JSON into our StructuredLlmResponse format
    /// </summary>
    private StructuredLlmResponse ParseStructuredJson(string json)
    {
        // This is a simplified version - in a real implementation, you'd handle
        // different provider formats (OpenAI, Anthropic, Azure, etc.)

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        var response = new StructuredLlmResponse();

        // Extract text content
        if (root.TryGetProperty("content", out var contentElement))
        {
            // Check if content is a string or array (Anthropic uses array)
            if (contentElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                response.TextContent = contentElement.GetString();
            }
            else if (contentElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                // Anthropic format - process content blocks
                foreach (var block in contentElement.EnumerateArray())
                {
                    ProcessContentBlock(block, response);
                }
            }
        }
        else if (root.TryGetProperty("message", out var messageElement) &&
                 messageElement.TryGetProperty("content", out var msgContentElement))
        {
            if (msgContentElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                response.TextContent = msgContentElement.GetString();
            }
        }

        // Extract tool calls - check multiple locations
        // Direct tool_calls at root
        if (root.TryGetProperty("tool_calls", out var toolCallsElement))
        {
            foreach (var toolCallElement in toolCallsElement.EnumerateArray())
            {
                var toolCall = ParseToolCallElement(toolCallElement);
                if (toolCall != null)
                {
                    response.ToolCalls.Add(toolCall);
                }
            }
        }

        // OpenAI format - tool_calls inside choices[0].message
        if (root.TryGetProperty("choices", out var choicesElement) &&
            choicesElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var choice in choicesElement.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var messageElement))
                {
                    // Extract content if not already extracted
                    if (string.IsNullOrEmpty(response.TextContent) &&
                        messageElement.TryGetProperty("content", out var contentProp))
                    {
                        if (contentProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            response.TextContent = contentProp.GetString();
                        }
                    }

                    // Extract tool calls from message
                    if (messageElement.TryGetProperty("tool_calls", out var msgToolCallsElement))
                    {
                        foreach (var toolCallElement in msgToolCallsElement.EnumerateArray())
                        {
                            var toolCall = ParseToolCallElement(toolCallElement);
                            if (toolCall != null)
                            {
                                response.ToolCalls.Add(toolCall);
                            }
                        }
                    }
                }

                // Get finish reason
                if (choice.TryGetProperty("finish_reason", out var finishReasonProp))
                {
                    response.Metadata.FinishReason = finishReasonProp.GetString();
                }
            }
        }

        // Extract metadata
        response.Metadata.Provider = "structured";
        response.Metadata.Timestamp = DateTime.UtcNow;

        if (root.TryGetProperty("model", out var modelElement))
        {
            response.Metadata.Model = modelElement.GetString() ?? "";
        }

        if (root.TryGetProperty("finish_reason", out var finishReasonElement))
        {
            response.Metadata.FinishReason = finishReasonElement.GetString();
        }

        return response;
    }

    /// <summary>
    /// Process a content block (Anthropic format)
    /// </summary>
    private void ProcessContentBlock(System.Text.Json.JsonElement block, StructuredLlmResponse response)
    {
        if (!block.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();

        switch (type)
        {
            case "text":
                if (block.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        response.TextContent = string.IsNullOrEmpty(response.TextContent)
                            ? text
                            : response.TextContent + "\n" + text;
                    }
                }
                break;

            case "tool_use":
                var toolCall = ParseAnthropicToolUse(block);
                if (toolCall != null)
                {
                    response.ToolCalls.Add(toolCall);
                }
                break;
        }
    }

    /// <summary>
    /// Parse Anthropic tool_use block
    /// </summary>
    private StructuredToolCall? ParseAnthropicToolUse(System.Text.Json.JsonElement element)
    {
        try
        {
            var id = element.TryGetProperty("id", out var idProp)
                ? idProp.GetString() ?? ""
                : "";

            var name = element.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? ""
                : "";

            var argumentsJson = "{}";
            if (element.TryGetProperty("input", out var inputProp))
            {
                argumentsJson = inputProp.GetRawText();
            }

            if (!string.IsNullOrEmpty(name))
            {
                return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a single tool call element from JSON
    /// </summary>
    private static StructuredToolCall? ParseToolCallElement(System.Text.Json.JsonElement element)
    {
        try
        {
            var id = "";
            var name = "";
            var argumentsJson = "";

            if (element.TryGetProperty("id", out var idElement))
            {
                id = idElement.GetString() ?? "";
            }

            if (element.TryGetProperty("function", out var functionElement))
            {
                if (functionElement.TryGetProperty("name", out var nameElement))
                {
                    name = nameElement.GetString() ?? "";
                }

                if (functionElement.TryGetProperty("arguments", out var argsElement))
                {
                    // Arguments might be a string containing JSON
                    if (argsElement.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        argumentsJson = argsElement.GetString() ?? "{}";
                    }
                    else
                    {
                        argumentsJson = argsElement.GetRawText();
                    }
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Synchronous version of ParseStructuredResponseAsync for interface compatibility
    /// </summary>
    private ResponseNode ParseStructuredResponseSync(StructuredLlmResponse structuredResponse)
    {
        var responseNode = new ResponseNode
        {
            ModelProvider = structuredResponse.Metadata.Provider,
            ModelName = structuredResponse.Metadata.Model,
            Timestamp = structuredResponse.Metadata.Timestamp,
            ResponseMetadata = new ResponseMetadata
            {
                IsComplete = string.IsNullOrEmpty(structuredResponse.Metadata.FinishReason) ||
                           structuredResponse.Metadata.FinishReason != "length",
                FinishReason = structuredResponse.Metadata.FinishReason,
                TokenCount = structuredResponse.Metadata.Usage?.TotalTokens ?? 0
            }
        };

        // Add text content if present (parse as plain text for structured responses)
        if (!string.IsNullOrWhiteSpace(structuredResponse.TextContent))
        {
            responseNode.Children.Add(new TextNode
            {
                Content = structuredResponse.TextContent,
                Format = TextFormat.Plain
            });
        }

        // Add structured tool calls
        foreach (var toolCall in structuredResponse.ToolCalls)
        {
            var toolCallNode = new ToolCallNode
            {
                CallId = toolCall.Id,
                ToolName = toolCall.Name,
                Arguments = toolCall.Arguments ?? new Dictionary<string, object?>(),
                IsComplete = toolCall.Arguments != null,
                ParseError = toolCall.ParseError
            };

            if (!string.IsNullOrEmpty(toolCall.ArgumentsJson))
            {
                toolCallNode.Metadata["RawArgumentsJson"] = toolCall.ArgumentsJson;
            }

            responseNode.Children.Add(toolCallNode);
        }

        // Add tool results
        foreach (var toolResult in structuredResponse.ToolResults)
        {
            responseNode.Children.Add(new ToolResultNode
            {
                CallId = toolResult.CallId,
                ToolName = toolResult.ToolName,
                Result = toolResult.Result,
                IsSuccess = toolResult.IsSuccess,
                ErrorMessage = toolResult.ErrorMessage,
                ExecutionError = toolResult.ExecutionError
            });
        }

        return responseNode;
    }

    /// <summary>
    /// Creates an error response when all parsing fails
    /// </summary>
    private ResponseNode CreateErrorResponse(Exception error, string originalInput)
    {
        var responseNode = new ResponseNode
        {
            ModelProvider = "unknown",
            Timestamp = DateTime.UtcNow,
            ResponseMetadata = new ResponseMetadata
            {
                IsComplete = false,
                Warnings = { $"Parsing failed: {error.Message}" }
            }
        };

        responseNode.Children.Add(new ErrorNode
        {
            Message = $"Failed to parse LLM response: {error.Message}",
            Severity = ErrorSeverity.Error,
            ErrorCode = error.GetType().Name
        });

        // Include original input as text for debugging
        if (!string.IsNullOrWhiteSpace(originalInput))
        {
            responseNode.Children.Add(new TextNode
            {
                Content = originalInput,
                Format = TextFormat.Plain
            });
        }

        return responseNode;
    }

    /// <summary>
    /// Creates an empty response for null/empty input
    /// </summary>
    private ResponseNode CreateEmptyResponse()
    {
        return new ResponseNode
        {
            ModelProvider = "hybrid",
            Timestamp = DateTime.UtcNow,
            ResponseMetadata = new ResponseMetadata
            {
                IsComplete = true,
                FinishReason = "empty_input"
            }
        };
    }
}

/// <summary>
/// Default implementation of IStructuredResponseFactory
/// This is now deprecated in favor of the full StructuredResponseFactory implementation
/// </summary>
[Obsolete("Use StructuredResponseFactory instead of DefaultStructuredResponseFactory")]
public class DefaultStructuredResponseFactory : StructuredResponseFactory
{
    public DefaultStructuredResponseFactory() : base(null)
    {
    }
}
