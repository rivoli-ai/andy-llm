using System.ClientModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// OpenAI provider implementation using the official OpenAI SDK
/// </summary>
public class OpenAIProvider : ILlmProvider
{
    private readonly OpenAIClient _openAiClient;
    private readonly ChatClient _chatClient;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;
    private readonly ProviderConfig _config;
    private readonly string _defaultModel;

    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    public string Name => "OpenAI";

    /// <summary>
    /// Initializes a new instance of the OpenAI provider
    /// </summary>
    /// <param name="options">Llm options</param>
    /// <param name="logger">Logger</param>
    /// <param name="httpClientFactory">Optional HTTP client factory (for models endpoint)</param>
    public OpenAIProvider(
        IOptions<LlmOptions> options,
        ILogger<OpenAIProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var llmOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (!llmOptions.Providers.TryGetValue("openai", out var config))
        {
            throw new InvalidOperationException("OpenAI provider configuration not found");
        }

        _config = config;

        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            // Try environment variable
            _config.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(_config.ApiKey))
            {
                throw new InvalidOperationException("OpenAI API key not configured");
            }
        }

        _defaultModel = _config.Model ?? llmOptions.DefaultModel ?? "gpt-4o";

        // Create OpenAI client
        var openAiOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(_config.ApiBase))
        {
            openAiOptions.Endpoint = new Uri(_config.ApiBase);
        }
        if (!string.IsNullOrEmpty(_config.Organization))
        {
            openAiOptions.OrganizationId = _config.Organization;
        }

        _openAiClient = new OpenAIClient(new ApiKeyCredential(_config.ApiKey), openAiOptions);
        _chatClient = _openAiClient.GetChatClient(_defaultModel);

        // Create HTTP client for models endpoint
        _httpClient = httpClientFactory?.CreateClient("OpenAI") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_config.ApiBase ?? "https://api.openai.com/v1/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        if (!string.IsNullOrEmpty(_config.Organization))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _config.Organization);
        }

        _logger.LogInformation("OpenAI provider initialized with model: {Model}", _defaultModel);
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
            _logger.LogWarning(ex, "OpenAI provider availability check failed");
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenAI completion");
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

            // Handle tool calls
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
                    {
                        accumulated.Id = toolUpdate.ToolCallId;
                    }

                    if (!string.IsNullOrEmpty(toolUpdate.FunctionName))
                    {
                        accumulated.Name = toolUpdate.FunctionName;
                    }

                    if (toolUpdate.FunctionArgumentsUpdate != null)
                    {
                        accumulated.Arguments += toolUpdate.FunctionArgumentsUpdate.ToString();

                        // Emit partial function call delta
                        if (!string.IsNullOrEmpty(accumulated.Name))
                        {
                            var partialCall = new FunctionCall
                            {
                                Id = string.IsNullOrEmpty(accumulated.Id) ? $"partial_{index}" : accumulated.Id,
                                Name = accumulated.Name,
                                Arguments = new Dictionary<string, object?>(),
                                ArgumentsJson = accumulated.Arguments
                            };

                            yield return new LlmStreamResponse
                            {
                                FunctionCall = partialCall,
                                IsComplete = false
                            };
                        }
                    }

                    // Check if tool call is complete
                    if (IsToolCallComplete(accumulated))
                    {
                        var functionCall = new FunctionCall
                        {
                            Id = accumulated.Id ?? $"call_{Guid.NewGuid():N}".Substring(0, 8),
                            Name = accumulated.Name,
                            Arguments = ParseArguments(accumulated.Arguments),
                            ArgumentsJson = accumulated.Arguments
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
                // Emit any remaining tool calls
                foreach (var (_, accumulated) in accumulatedToolCalls)
                {
                    if (IsToolCallComplete(accumulated))
                    {
                        var functionCall = new FunctionCall
                        {
                            Id = accumulated.Id ?? $"call_{Guid.NewGuid():N}".Substring(0, 8),
                            Name = accumulated.Name,
                            Arguments = ParseArguments(accumulated.Arguments),
                            ArgumentsJson = accumulated.Arguments
                        };

                        yield return new LlmStreamResponse
                        {
                            FunctionCall = functionCall,
                            IsComplete = false
                        };
                    }
                }

                yield return new LlmStreamResponse
                {
                    IsComplete = true,
                    FinishReason = update.FinishReason.ToString()
                };
            }
        }
    }

    /// <summary>
    /// Lists available models from OpenAI.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of available models.</returns>
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content);

            if (modelsResponse?.Data == null)
            {
                return Enumerable.Empty<ModelInfo>();
            }

            return modelsResponse.Data
                .Select(model => new ModelInfo
                {
                    Id = model.Id ?? string.Empty,
                    Name = model.Id ?? string.Empty,
                    Provider = "openai",
                    Created = model.Created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(model.Created.Value).DateTime : null,
                    Description = GetModelDescription(model.Id),
                    Family = GetModelFamily(model.Id),
                    ParameterSize = GetParameterSize(model.Id),
                    MaxTokens = GetMaxTokens(model.Id),
                    SupportsFunctions = SupportsFunctionCalling(model.Id),
                    SupportsVision = SupportsVision(model.Id),
                    IsFineTuned = model.Id?.Contains("ft-") ?? false,
                    Metadata = new Dictionary<string, object>
                    {
                        ["owned_by"] = model.OwnedBy ?? "unknown"
                    }
                })
                .OrderBy(m => m.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list OpenAI models");
            return Enumerable.Empty<ModelInfo>();
        }
    }

    private static string? GetModelDescription(string? modelId)
    {
        if (modelId == null)
        {
            return null;
        }

        return modelId switch
        {
            var id when id.StartsWith("gpt-4o") => "Most capable multimodal model",
            var id when id.StartsWith("gpt-4-turbo") => "High performance model with vision support",
            var id when id.StartsWith("gpt-4") => "Advanced reasoning model",
            var id when id.StartsWith("gpt-3.5-turbo") => "Fast and efficient model",
            var id when id.StartsWith("dall-e") => "Image generation model",
            var id when id.StartsWith("whisper") => "Speech recognition model",
            var id when id.StartsWith("tts") => "Text-to-speech model",
            var id when id.StartsWith("text-embedding") => "Embedding model",
            _ => null
        };
    }

    private static string? GetModelFamily(string? modelId)
    {
        if (modelId == null)
        {
            return null;
        }

        return modelId switch
        {
            var id when id.StartsWith("gpt-4o") => "GPT-4o",
            var id when id.StartsWith("gpt-4") => "GPT-4",
            var id when id.StartsWith("gpt-3.5") => "GPT-3.5",
            var id when id.StartsWith("dall-e") => "DALL-E",
            var id when id.StartsWith("whisper") => "Whisper",
            var id when id.StartsWith("tts") => "TTS",
            var id when id.StartsWith("text-embedding") => "Embedding",
            _ => null
        };
    }

    private static string? GetParameterSize(string? modelId)
    {
        if (modelId == null)
        {
            return null;
        }

        // OpenAI doesn't disclose exact parameter counts
        return modelId switch
        {
            var id when id.StartsWith("gpt-4o") => "Large",
            var id when id.StartsWith("gpt-4") => "Large",
            var id when id.StartsWith("gpt-3.5") => "Medium",
            _ => null
        };
    }

    private static int? GetMaxTokens(string? modelId)
    {
        if (modelId == null)
        {
            return null;
        }

        return modelId switch
        {
            var id when id.Contains("gpt-4o") => 128000,
            var id when id.Contains("gpt-4-turbo") => 128000,
            var id when id.Contains("gpt-4-32k") => 32768,
            var id when id.Contains("gpt-4") && !id.Contains("32k") => 8192,
            var id when id.Contains("gpt-3.5-turbo-16k") => 16385,
            var id when id.Contains("gpt-3.5-turbo") => 4096,
            _ => null
        };
    }

    private static bool SupportsFunctionCalling(string? modelId)
    {
        if (modelId == null)
        {
            return false;
        }

        return modelId.StartsWith("gpt-") && !modelId.Contains("instruct");
    }

    private static bool SupportsVision(string? modelId)
    {
        if (modelId == null)
        {
            return false;
        }

        return modelId.Contains("gpt-4o") ||
               modelId.Contains("gpt-4-turbo") ||
               modelId.Contains("vision");
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
                    var toolCalls = toolCallParts.Select(tcp => ChatToolCall.CreateFunctionToolCall(
                        tcp.CallId,
                        tcp.ToolName,
                        BinaryData.FromString(JsonSerializer.Serialize(tcp.Arguments))
                    )).ToList();

                    if (textParts.Any())
                    {
                        var assistantMessage = new AssistantChatMessage(string.Join(" ", textParts.Select(p => p.Text)));
                        foreach (var tc in toolCalls)
                        {
                            assistantMessage.ToolCalls.Add(tc);
                        }
                        yield return assistantMessage;
                    }
                    else
                    {
                        var assistantMessage = new AssistantChatMessage("");
                        foreach (var tc in toolCalls)
                        {
                            assistantMessage.ToolCalls.Add(tc);
                        }
                        yield return assistantMessage;
                    }
                }
                else if (textParts.Any())
                {
                    yield return new AssistantChatMessage(string.Join(" ", textParts.Select(p => p.Text)));
                }
                break;

            case MessageRole.Tool:
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

        // Add tools if present
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
                    Arguments = ParseArguments(toolCall.FunctionArguments?.ToString() ?? "{}"),
                    ArgumentsJson = toolCall.FunctionArguments?.ToString()
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
            {
                return new Dictionary<string, object?>();
            }

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
        {
            return false;
        }

        var trimmedArgs = toolCall.Arguments.TrimEnd();
        if (!trimmedArgs.StartsWith('{') || !trimmedArgs.EndsWith('}'))
        {
            return false;
        }

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

    // OpenAI Models API response classes
    private class OpenAIModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIModel>? Data { get; set; }
    }

    private class OpenAIModel
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("created")]
        public long? Created { get; set; }

        [JsonPropertyName("owned_by")]
        public string? OwnedBy { get; set; }
    }
}
