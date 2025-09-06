using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Parsing;

/// <summary>
/// Factory for creating structured responses from provider-specific formats
/// </summary>
public class StructuredResponseFactory : IStructuredResponseFactory
{
    private readonly ILogger<StructuredResponseFactory>? _logger;

    public StructuredResponseFactory(ILogger<StructuredResponseFactory>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a structured response from OpenAI-style API response
    /// </summary>
    public StructuredLlmResponse CreateFromOpenAI(object openAIResponse)
    {
        if (openAIResponse == null)
            throw new ArgumentNullException(nameof(openAIResponse));

        var response = new StructuredLlmResponse();

        try
        {
            // Handle different types of OpenAI responses
            if (openAIResponse is string jsonString)
            {
                return ParseOpenAIJson(jsonString);
            }

            // Handle OpenAI ChatCompletion object from SDK
            var responseType = openAIResponse.GetType();
            
            // Check if it's an OpenAI ChatCompletion type
            if (responseType.Name == "ChatCompletion")
            {
                response = ExtractFromOpenAIChatCompletion(openAIResponse);
            }
            else if (responseType.Name == "StreamingChatCompletionUpdate")
            {
                response = ExtractFromOpenAIStreamingUpdate(openAIResponse);
            }
            else
            {
                // Try generic JSON serialization
                var json = JsonSerializer.Serialize(openAIResponse);
                return ParseOpenAIJson(json);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error creating structured response from OpenAI format");
            response.Metadata.Provider = "openai";
            response.TextContent = openAIResponse.ToString();
        }

        return response;
    }

    /// <summary>
    /// Creates a structured response from Anthropic-style API response
    /// </summary>
    public StructuredLlmResponse CreateFromAnthropic(object anthropicResponse)
    {
        if (anthropicResponse == null)
            throw new ArgumentNullException(nameof(anthropicResponse));

        var response = new StructuredLlmResponse();

        try
        {
            if (anthropicResponse is string jsonString)
            {
                return ParseAnthropicJson(jsonString);
            }

            // Handle Anthropic message object
            var responseType = anthropicResponse.GetType();
            
            if (responseType.Name.Contains("Message") || responseType.Name.Contains("Claude"))
            {
                response = ExtractFromAnthropicMessage(anthropicResponse);
            }
            else
            {
                // Try generic JSON serialization
                var json = JsonSerializer.Serialize(anthropicResponse);
                return ParseAnthropicJson(json);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error creating structured response from Anthropic format");
            response.Metadata.Provider = "anthropic";
            response.TextContent = anthropicResponse.ToString();
        }

        return response;
    }

    /// <summary>
    /// Creates a structured response from generic provider format
    /// </summary>
    public StructuredLlmResponse CreateFromGeneric(string textContent, object? toolCallsData)
    {
        var response = new StructuredLlmResponse
        {
            TextContent = textContent,
            Metadata = new StructuredResponseMetadata
            {
                Provider = "generic",
                Timestamp = DateTime.UtcNow
            }
        };

        if (toolCallsData != null)
        {
            try
            {
                var json = JsonSerializer.Serialize(toolCallsData);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        var toolCall = ParseGenericToolCall(element);
                        if (toolCall != null)
                        {
                            response.ToolCalls.Add(toolCall);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error parsing generic tool calls data");
            }
        }

        return response;
    }

    private StructuredLlmResponse ParseOpenAIJson(string json)
    {
        var response = new StructuredLlmResponse
        {
            Metadata = new StructuredResponseMetadata
            {
                Provider = "openai",
                Timestamp = DateTime.UtcNow
            }
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Handle standard OpenAI response format
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var message))
                    {
                        ExtractOpenAIMessage(message, response);
                    }
                    
                    if (choice.TryGetProperty("finish_reason", out var finishReason))
                    {
                        response.Metadata.FinishReason = finishReason.GetString();
                    }
                }
            }
            // Handle direct message format
            else if (root.TryGetProperty("content", out _) || root.TryGetProperty("tool_calls", out _))
            {
                ExtractOpenAIMessage(root, response);
            }
            // Handle message wrapper format (without choices array)
            else if (root.TryGetProperty("message", out var message))
            {
                ExtractOpenAIMessage(message, response);
            }

            // Extract model information
            if (root.TryGetProperty("model", out var model))
            {
                response.Metadata.Model = model.GetString() ?? "";
            }

            // Extract usage information
            if (root.TryGetProperty("usage", out var usage))
            {
                response.Metadata.Usage = ParseUsage(usage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing OpenAI JSON response");
            response.TextContent = json;
        }

        return response;
    }

    private void ExtractOpenAIMessage(JsonElement message, StructuredLlmResponse response)
    {
        // Extract content
        if (message.TryGetProperty("content", out var content))
        {
            response.TextContent = content.GetString();
        }

        // Extract tool calls
        if (message.TryGetProperty("tool_calls", out var toolCalls) && 
            toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCallElement in toolCalls.EnumerateArray())
            {
                var toolCall = ParseOpenAIToolCall(toolCallElement);
                if (toolCall != null)
                {
                    response.ToolCalls.Add(toolCall);
                }
            }
        }

        // Extract function call (older format)
        if (message.TryGetProperty("function_call", out var functionCall))
        {
            var toolCall = ParseOpenAIFunctionCall(functionCall);
            if (toolCall != null)
            {
                response.ToolCalls.Add(toolCall);
            }
        }
    }

    private StructuredToolCall? ParseOpenAIToolCall(JsonElement element)
    {
        try
        {
            var id = element.TryGetProperty("id", out var idProp) 
                ? idProp.GetString() ?? "" 
                : Guid.NewGuid().ToString();
                
            var type = element.TryGetProperty("type", out var typeProp) 
                ? typeProp.GetString() ?? "function" 
                : "function";

            if (type != "function")
            {
                _logger?.LogWarning("Unsupported tool call type: {Type}", type);
                return null;
            }

            if (!element.TryGetProperty("function", out var function))
            {
                return null;
            }

            var name = function.TryGetProperty("name", out var nameProp) 
                ? nameProp.GetString() ?? "" 
                : "";
                
            var argumentsJson = function.TryGetProperty("arguments", out var argsProp) 
                ? argsProp.GetString() ?? "{}" 
                : "{}";

            return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing OpenAI tool call");
            return null;
        }
    }

    private StructuredToolCall? ParseOpenAIFunctionCall(JsonElement element)
    {
        try
        {
            var name = element.TryGetProperty("name", out var nameProp) 
                ? nameProp.GetString() ?? "" 
                : "";
                
            var argumentsJson = element.TryGetProperty("arguments", out var argsProp) 
                ? argsProp.GetString() ?? "{}" 
                : "{}";

            var id = $"func_{Guid.NewGuid():N}";
            return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing OpenAI function call");
            return null;
        }
    }

    private StructuredLlmResponse ParseAnthropicJson(string json)
    {
        var response = new StructuredLlmResponse
        {
            Metadata = new StructuredResponseMetadata
            {
                Provider = "anthropic",
                Timestamp = DateTime.UtcNow
            }
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Extract content - Anthropic uses content array
            if (root.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentItem in content.EnumerateArray())
                    {
                        ProcessAnthropicContentItem(contentItem, response);
                    }
                }
                else if (content.ValueKind == JsonValueKind.String)
                {
                    response.TextContent = content.GetString();
                }
            }

            // Extract model
            if (root.TryGetProperty("model", out var model))
            {
                response.Metadata.Model = model.GetString() ?? "";
            }

            // Extract stop reason
            if (root.TryGetProperty("stop_reason", out var stopReason))
            {
                response.Metadata.FinishReason = stopReason.GetString();
            }

            // Extract usage
            if (root.TryGetProperty("usage", out var usage))
            {
                response.Metadata.Usage = ParseUsage(usage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing Anthropic JSON response");
            response.TextContent = json;
        }

        return response;
    }

    private void ProcessAnthropicContentItem(JsonElement item, StructuredLlmResponse response)
    {
        if (!item.TryGetProperty("type", out var type))
            return;

        var typeStr = type.GetString();
        
        switch (typeStr)
        {
            case "text":
                if (item.TryGetProperty("text", out var text))
                {
                    var textContent = text.GetString();
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        response.TextContent = string.IsNullOrEmpty(response.TextContent) 
                            ? textContent 
                            : response.TextContent + "\n" + textContent;
                    }
                }
                break;
                
            case "tool_use":
                var toolCall = ParseAnthropicToolUse(item);
                if (toolCall != null)
                {
                    response.ToolCalls.Add(toolCall);
                }
                break;
                
            case "tool_result":
                var toolResult = ParseAnthropicToolResult(item);
                if (toolResult != null)
                {
                    response.ToolResults.Add(toolResult);
                }
                break;
        }
    }

    private StructuredToolCall? ParseAnthropicToolUse(JsonElement element)
    {
        try
        {
            var id = element.TryGetProperty("id", out var idProp) 
                ? idProp.GetString() ?? Guid.NewGuid().ToString() 
                : Guid.NewGuid().ToString();
                
            var name = element.TryGetProperty("name", out var nameProp) 
                ? nameProp.GetString() ?? "" 
                : "";
                
            var argumentsJson = "{}";
            if (element.TryGetProperty("input", out var input))
            {
                argumentsJson = input.GetRawText();
            }

            return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing Anthropic tool use");
            return null;
        }
    }

    private StructuredToolResult? ParseAnthropicToolResult(JsonElement element)
    {
        try
        {
            var result = new StructuredToolResult
            {
                CallId = element.TryGetProperty("tool_use_id", out var idProp) 
                    ? idProp.GetString() ?? "" 
                    : "",
                IsSuccess = !element.TryGetProperty("is_error", out var errorProp) || 
                           !errorProp.GetBoolean()
            };

            if (element.TryGetProperty("content", out var content))
            {
                result.Result = content.GetRawText();
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing Anthropic tool result");
            return null;
        }
    }

    private StructuredToolCall? ParseGenericToolCall(JsonElement element)
    {
        try
        {
            // Try common patterns
            var id = element.TryGetProperty("id", out var idProp) 
                ? idProp.GetString() ?? Guid.NewGuid().ToString() 
                : element.TryGetProperty("call_id", out var callIdProp) 
                    ? callIdProp.GetString() ?? Guid.NewGuid().ToString()
                    : Guid.NewGuid().ToString();

            var name = element.TryGetProperty("name", out var nameProp) 
                ? nameProp.GetString() ?? ""
                : element.TryGetProperty("function", out var funcProp) 
                    ? funcProp.GetString() ?? ""
                    : element.TryGetProperty("tool", out var toolProp) 
                        ? toolProp.GetString() ?? ""
                        : "";

            var argumentsJson = "{}";
            if (element.TryGetProperty("arguments", out var args))
            {
                argumentsJson = args.ValueKind == JsonValueKind.String 
                    ? args.GetString() ?? "{}" 
                    : args.GetRawText();
            }
            else if (element.TryGetProperty("parameters", out var parameters))
            {
                argumentsJson = parameters.ValueKind == JsonValueKind.String 
                    ? parameters.GetString() ?? "{}" 
                    : parameters.GetRawText();
            }
            else if (element.TryGetProperty("input", out var input))
            {
                argumentsJson = input.ValueKind == JsonValueKind.String 
                    ? input.GetString() ?? "{}" 
                    : input.GetRawText();
            }

            if (string.IsNullOrEmpty(name))
                return null;

            return StructuredArgumentParser.CreateToolCall(id, name, argumentsJson);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing generic tool call");
            return null;
        }
    }

    private TokenUsage? ParseUsage(JsonElement usage)
    {
        try
        {
            return new TokenUsage
            {
                InputTokens = usage.TryGetProperty("prompt_tokens", out var promptTokens) 
                    ? promptTokens.GetInt32() 
                    : usage.TryGetProperty("input_tokens", out var inputTokens) 
                        ? inputTokens.GetInt32() 
                        : 0,
                OutputTokens = usage.TryGetProperty("completion_tokens", out var completionTokens) 
                    ? completionTokens.GetInt32() 
                    : usage.TryGetProperty("output_tokens", out var outputTokens) 
                        ? outputTokens.GetInt32() 
                        : 0,
                TotalTokens = usage.TryGetProperty("total_tokens", out var totalTokens) 
                    ? totalTokens.GetInt32() 
                    : 0
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error parsing token usage");
            return null;
        }
    }

    private StructuredLlmResponse ExtractFromOpenAIChatCompletion(object chatCompletion)
    {
        var response = new StructuredLlmResponse
        {
            Metadata = new StructuredResponseMetadata
            {
                Provider = "openai",
                Timestamp = DateTime.UtcNow
            }
        };

        try
        {
            // Use reflection to extract data from OpenAI SDK types
            var type = chatCompletion.GetType();
            
            // Get Content
            var contentProp = type.GetProperty("Content");
            if (contentProp != null)
            {
                var content = contentProp.GetValue(chatCompletion);
                if (content != null)
                {
                    var contentList = content as System.Collections.IEnumerable;
                    if (contentList != null)
                    {
                        foreach (var item in contentList)
                        {
                            var textProp = item.GetType().GetProperty("Text");
                            if (textProp != null)
                            {
                                var text = textProp.GetValue(item) as string;
                                if (!string.IsNullOrEmpty(text))
                                {
                                    response.TextContent = string.IsNullOrEmpty(response.TextContent) 
                                        ? text 
                                        : response.TextContent + text;
                                }
                            }
                        }
                    }
                }
            }

            // Get Model
            var modelProp = type.GetProperty("Model");
            if (modelProp != null)
            {
                response.Metadata.Model = modelProp.GetValue(chatCompletion)?.ToString() ?? "";
            }

            // Get Finish Reason
            var finishReasonProp = type.GetProperty("FinishReason");
            if (finishReasonProp != null)
            {
                response.Metadata.FinishReason = finishReasonProp.GetValue(chatCompletion)?.ToString();
            }

            // Get Tool Calls
            var toolCallsProp = type.GetProperty("ToolCalls");
            if (toolCallsProp != null)
            {
                var toolCalls = toolCallsProp.GetValue(chatCompletion) as System.Collections.IEnumerable;
                if (toolCalls != null)
                {
                    foreach (var toolCall in toolCalls)
                    {
                        var tcType = toolCall.GetType();
                        var id = tcType.GetProperty("Id")?.GetValue(toolCall)?.ToString() ?? "";
                        var functionProp = tcType.GetProperty("Function");
                        
                        if (functionProp != null)
                        {
                            var function = functionProp.GetValue(toolCall);
                            if (function != null)
                            {
                                var funcType = function.GetType();
                                var name = funcType.GetProperty("Name")?.GetValue(function)?.ToString() ?? "";
                                var args = funcType.GetProperty("Arguments")?.GetValue(function)?.ToString() ?? "{}";
                                
                                response.ToolCalls.Add(StructuredArgumentParser.CreateToolCall(id, name, args));
                            }
                        }
                    }
                }
            }

            // Get Usage
            var usageProp = type.GetProperty("Usage");
            if (usageProp != null)
            {
                var usage = usageProp.GetValue(chatCompletion);
                if (usage != null)
                {
                    var usageType = usage.GetType();
                    response.Metadata.Usage = new TokenUsage
                    {
                        InputTokens = (int)(usageType.GetProperty("InputTokens")?.GetValue(usage) ?? 0),
                        OutputTokens = (int)(usageType.GetProperty("OutputTokens")?.GetValue(usage) ?? 0),
                        TotalTokens = (int)(usageType.GetProperty("TotalTokens")?.GetValue(usage) ?? 0)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error extracting from OpenAI ChatCompletion object");
        }

        return response;
    }

    private StructuredLlmResponse ExtractFromOpenAIStreamingUpdate(object streamingUpdate)
    {
        var response = new StructuredLlmResponse
        {
            Metadata = new StructuredResponseMetadata
            {
                Provider = "openai",
                Timestamp = DateTime.UtcNow
            }
        };

        try
        {
            var type = streamingUpdate.GetType();
            
            // Get Content Update
            var contentUpdateProp = type.GetProperty("ContentUpdate");
            if (contentUpdateProp != null)
            {
                var contentUpdate = contentUpdateProp.GetValue(streamingUpdate);
                if (contentUpdate != null)
                {
                    var textProp = contentUpdate.GetType().GetProperty("Text");
                    if (textProp != null)
                    {
                        response.TextContent = textProp.GetValue(contentUpdate)?.ToString();
                    }
                }
            }

            // Get Finish Reason
            var finishReasonProp = type.GetProperty("FinishReason");
            if (finishReasonProp != null)
            {
                response.Metadata.FinishReason = finishReasonProp.GetValue(streamingUpdate)?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error extracting from OpenAI streaming update");
        }

        return response;
    }

    private StructuredLlmResponse ExtractFromAnthropicMessage(object message)
    {
        var response = new StructuredLlmResponse
        {
            Metadata = new StructuredResponseMetadata
            {
                Provider = "anthropic",
                Timestamp = DateTime.UtcNow
            }
        };

        try
        {
            // Use reflection for Anthropic SDK types
            var type = message.GetType();
            
            // Extract content
            var contentProp = type.GetProperty("Content");
            if (contentProp != null)
            {
                var content = contentProp.GetValue(message);
                if (content != null)
                {
                    // Anthropic typically uses content blocks
                    if (content is System.Collections.IEnumerable contentList)
                    {
                        foreach (var block in contentList)
                        {
                            ProcessAnthropicContentBlock(block, response);
                        }
                    }
                    else
                    {
                        response.TextContent = content.ToString();
                    }
                }
            }

            // Extract model
            var modelProp = type.GetProperty("Model");
            if (modelProp != null)
            {
                response.Metadata.Model = modelProp.GetValue(message)?.ToString() ?? "";
            }

            // Extract stop reason
            var stopReasonProp = type.GetProperty("StopReason");
            if (stopReasonProp != null)
            {
                response.Metadata.FinishReason = stopReasonProp.GetValue(message)?.ToString();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error extracting from Anthropic message object");
        }

        return response;
    }

    private void ProcessAnthropicContentBlock(object block, StructuredLlmResponse response)
    {
        try
        {
            var type = block.GetType();
            var typeName = type.Name;

            if (typeName.Contains("Text"))
            {
                var textProp = type.GetProperty("Text");
                if (textProp != null)
                {
                    var text = textProp.GetValue(block)?.ToString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        response.TextContent = string.IsNullOrEmpty(response.TextContent) 
                            ? text 
                            : response.TextContent + "\n" + text;
                    }
                }
            }
            else if (typeName.Contains("ToolUse"))
            {
                var id = type.GetProperty("Id")?.GetValue(block)?.ToString() ?? Guid.NewGuid().ToString();
                var name = type.GetProperty("Name")?.GetValue(block)?.ToString() ?? "";
                var input = type.GetProperty("Input")?.GetValue(block);
                
                var argumentsJson = input != null ? JsonSerializer.Serialize(input) : "{}";
                response.ToolCalls.Add(StructuredArgumentParser.CreateToolCall(id, name, argumentsJson));
            }
            else if (typeName.Contains("ToolResult"))
            {
                var result = new StructuredToolResult
                {
                    CallId = type.GetProperty("ToolUseId")?.GetValue(block)?.ToString() ?? "",
                    Result = type.GetProperty("Content")?.GetValue(block),
                    IsSuccess = true
                };
                
                var isErrorProp = type.GetProperty("IsError");
                if (isErrorProp != null)
                {
                    result.IsSuccess = !(bool)(isErrorProp.GetValue(block) ?? false);
                }
                
                response.ToolResults.Add(result);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing Anthropic content block");
        }
    }
}