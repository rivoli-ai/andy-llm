using System.ClientModel;
using ClientResultException = System.ClientModel.ClientResultException;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// Cerebras provider implementation using OpenAI-compatible API
/// </summary>
public class CerebrasProvider : ILlmProvider
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<CerebrasProvider> _logger;
    private readonly ProviderConfig _config;
    private readonly string _defaultModel;

    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    public string Name => "Cerebras";

    /// <summary>
    /// Initializes a new instance of the <see cref="CerebrasProvider"/> class.
    /// </summary>
    /// <param name="options">The LLM options.</param>
    /// <param name="logger">The logger.</param>
    public CerebrasProvider(
        IOptions<LlmOptions> options,
        ILogger<CerebrasProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        var llmOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        
        if (!llmOptions.Providers.TryGetValue("cerebras", out var config))
        {
            throw new InvalidOperationException("Cerebras provider configuration not found");
        }

        _config = config;
        
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            // Try environment variable
            _config.ApiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
            if (string.IsNullOrEmpty(_config.ApiKey))
            {
                throw new InvalidOperationException("Cerebras API key not configured");
            }
        }

        // Use llama-3.3-70b which supports tool calling
        _defaultModel = _config.Model ?? "llama-3.3-70b";

        // Cerebras uses OpenAI-compatible API
        var cerebrasOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_config.ApiBase ?? "https://api.cerebras.ai/v1")
        };

        // Use OpenAI SDK with Cerebras endpoint
        var openAiClient = new OpenAIClient(new ApiKeyCredential(_config.ApiKey), cerebrasOptions);
        _chatClient = openAiClient.GetChatClient(_defaultModel);
        
        _logger.LogInformation("Cerebras provider initialized with model: {Model}", _defaultModel);
    }

    /// <summary>
    /// Checks if the provider is available.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the provider is available; otherwise, false.</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try a simple completion to check availability
            var messages = new List<ChatMessage> { new SystemChatMessage("Test") };
            var options = new ChatCompletionOptions 
            { 
                MaxOutputTokenCount = 1,
                Temperature = 0 
            };
            
            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);
            return response?.Value != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cerebras provider availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Completes a chat request.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The LLM response.</returns>
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertMessages(request);
            var options = CreateCompletionOptions(request);

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            if (response?.Value == null)
            {
                return new LlmResponse
                {
                    Content = "",
                    FinishReason = "error"
                };
            }

            var completion = response.Value;
            var functionCalls = ExtractFunctionCalls(completion);

            return new LlmResponse
            {
                Content = completion.Content?.Count > 0 ? completion.Content[0]?.Text ?? "" : "",
                FunctionCalls = functionCalls,
                FinishReason = completion.FinishReason.ToString(),
                TokensUsed = completion.Usage?.TotalTokenCount,
                Model = completion.Model
            };
        }
        catch (ClientResultException ex)
        {
            _logger.LogError(ex, "Cerebras API error: Status={Status}, Message={Message}", ex.Status, ex.Message);
            return new LlmResponse
            {
                Content = $"Cerebras API error: {ex.Message}",
                FinishReason = "error"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Cerebras completion");
            return new LlmResponse
            {
                Content = "",
                FinishReason = "error"
            };
        }
    }

    /// <summary>
    /// Streams a chat completion response.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async stream of response chunks.</returns>
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertMessages(request);
        var options = CreateCompletionOptions(request);

        var accumulatedToolCalls = new Dictionary<int, AccumulatedToolCall>();

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken))
        {
            // Handle text content
            if (update.ContentUpdate?.Count > 0)
            {
                var text = string.Join("", update.ContentUpdate.Select(c => c.Text));
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new LlmStreamResponse
                    {
                        TextDelta = text,
                        IsComplete = false
                    };
                }
            }

            // Handle tool calls (if supported by Cerebras models)
            if (update.ToolCallUpdates?.Count > 0)
            {
                foreach (var toolUpdate in update.ToolCallUpdates)
                {
                    var index = toolUpdate.Index;
                    
                    if (!accumulatedToolCalls.TryGetValue(index, out var accumulated))
                    {
                        accumulated = new AccumulatedToolCall();
                        accumulatedToolCalls[index] = accumulated;
                    }

                    if (!string.IsNullOrEmpty(toolUpdate.ToolCallId))
                        accumulated.Id = toolUpdate.ToolCallId;
                    
                    if (!string.IsNullOrEmpty(toolUpdate.FunctionName))
                        accumulated.Name = toolUpdate.FunctionName;
                    
                    if (toolUpdate.FunctionArgumentsUpdate != null)
                        accumulated.Arguments += toolUpdate.FunctionArgumentsUpdate.ToString();

                    // Check if tool call is complete
                    if (IsToolCallComplete(accumulated))
                    {
                        var functionCall = new FunctionCall
                        {
                            Id = accumulated.Id ?? $"call_{Guid.NewGuid():N}".Substring(0, 8),
                            Name = accumulated.Name,
                            Arguments = ParseArguments(accumulated.Arguments)
                        };

                        yield return new LlmStreamResponse
                        {
                            FunctionCall = functionCall,
                            IsComplete = false
                        };

                        accumulatedToolCalls.Remove(index);
                    }
                }
            }

            // Handle completion
            if (update.FinishReason != null)
            {
                yield return new LlmStreamResponse
                {
                    IsComplete = true
                };
            }
        }
    }

    private List<ChatMessage> ConvertMessages(LlmRequest request)
    {
        var messages = new List<ChatMessage>();

        // Add system message if present
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        // Convert messages
        foreach (var message in request.Messages)
        {
            messages.AddRange(ConvertMessage(message));
        }

        return messages;
    }

    private IEnumerable<ChatMessage> ConvertMessage(Message message)
    {
        switch (message.Role)
        {
            case MessageRole.System:
                var systemText = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                yield return new SystemChatMessage(systemText);
                break;

            case MessageRole.User:
                var userText = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                yield return new UserChatMessage(userText);
                break;

            case MessageRole.Assistant:
                var textParts = message.Parts.OfType<TextPart>().ToList();
                var toolCallParts = message.Parts.OfType<ToolCallPart>().ToList();

                if (toolCallParts.Any())
                {
                    // Tool calls are supported by llama-3.3-70b
                    var toolCalls = new List<ChatToolCall>();
                    foreach (var toolCall in toolCallParts)
                    {
                        toolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                            toolCall.CallId,
                            toolCall.ToolName,
                            BinaryData.FromString(JsonSerializer.Serialize(toolCall.Arguments))
                        ));
                    }
                    
                    if (toolCalls.Any())
                    {
                        var assistantText = textParts.Any() ? string.Join(" ", textParts.Select(p => p.Text)) : "";
                        var assistantMessage = new AssistantChatMessage(assistantText);
                        foreach (var tc in toolCalls)
                        {
                            assistantMessage.ToolCalls.Add(tc);
                        }
                        yield return assistantMessage;
                        break;
                    }
                }
                
                if (textParts.Any())
                {
                    yield return new AssistantChatMessage(string.Join(" ", textParts.Select(p => p.Text)));
                }
                break;

            case MessageRole.Tool:
                // Tool responses are supported by llama-3.3-70b
                foreach (var part in message.Parts.OfType<ToolResponsePart>())
                {
                    var responseJson = JsonSerializer.Serialize(part.Response);
                    yield return new ToolChatMessage(part.CallId, responseJson);
                }
                break;
        }
    }

    private ChatCompletionOptions CreateCompletionOptions(LlmRequest request)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = (float?)request.Temperature,
            MaxOutputTokenCount = request.MaxTokens
        };

        // Add tools if present - llama-3.3-70b supports tool calling
        if (request.Tools?.Any() == true)
        {
            foreach (var tool in request.Tools)
            {
                var functionTool = ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description,
                    BinaryData.FromString(JsonSerializer.Serialize(tool.Parameters))
                );
                options.Tools.Add(functionTool);
            }
        }

        return options;
    }

    private List<FunctionCall> ExtractFunctionCalls(ChatCompletion completion)
    {
        var functionCalls = new List<FunctionCall>();

        if (completion.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in completion.ToolCalls)
            {
                functionCalls.Add(new FunctionCall
                {
                    Id = toolCall.Id,
                    Name = toolCall.FunctionName ?? "",
                    Arguments = ParseArguments(toolCall.FunctionArguments?.ToString() ?? "{}")
                });
            }
        }

        return functionCalls;
    }

    private static Dictionary<string, object?> ParseArguments(string? argumentsJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return new Dictionary<string, object?>();

            using var doc = JsonDocument.Parse(argumentsJson);
            var dict = new Dictionary<string, object?>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                dict[property.Name] = JsonElementToObject(property.Value);
            }

            return dict;
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonElementToObject).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static bool IsToolCallComplete(AccumulatedToolCall toolCall)
    {
        if (string.IsNullOrEmpty(toolCall.Name) || string.IsNullOrEmpty(toolCall.Arguments))
            return false;

        var trimmedArgs = toolCall.Arguments.TrimEnd();
        if (!trimmedArgs.StartsWith('{') || !trimmedArgs.EndsWith('}'))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmedArgs);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private class AccumulatedToolCall
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }
}