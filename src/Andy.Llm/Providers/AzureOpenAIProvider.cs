using System.ClientModel;
using Andy.Llm.Providers;
using Andy.Llm.Configuration;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// Provider for Azure OpenAI Service.
/// </summary>
public class AzureOpenAIProvider : Andy.Model.Llm.ILlmProvider
{
    private readonly ILogger<AzureOpenAIProvider> _logger;
    private readonly ProviderConfig _config;
    private readonly AzureOpenAIClient _azureClient;
    private readonly ChatClient _chatClient;
    private readonly string _deploymentName;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIProvider"/> class.
    /// </summary>
    /// <param name="options">The LLM options containing Azure OpenAI configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public AzureOpenAIProvider(
        IOptions<LlmOptions> options,
        ILogger<AzureOpenAIProvider> logger)
    {
        _logger = logger;

        // Load configuration
        _config = LoadConfiguration(options.Value);

        // Validate required settings
        var endpoint = _config.ApiBase ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var apiKey = _config.ApiKey ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
        _deploymentName = _config.DeploymentName ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4";

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException(
                "Azure OpenAI endpoint not configured. Set AZURE_OPENAI_ENDPOINT environment variable or configure in options.");
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Azure OpenAI API key not configured. Set AZURE_OPENAI_KEY environment variable or configure in options.");
        }

        // Create Azure OpenAI client
        _azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(apiKey));

        // Get chat client for deployment
        _chatClient = _azureClient.GetChatClient(_deploymentName);

        _logger.LogInformation("Azure OpenAI provider initialized with endpoint: {Endpoint}, deployment: {Deployment}",
            endpoint, _deploymentName);
    }

    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    public string Name => "azure";

    /// <summary>
    /// Checks if the Azure OpenAI service is available.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the service is available, false otherwise.</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try a minimal request to check availability
            var testRequest = new List<ChatMessage>
            {
                new SystemChatMessage("test")
            };

            var options = new ChatCompletionOptions();
            options.MaxOutputTokenCount = 1;

            await _chatClient.CompleteChatAsync(testRequest, options, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Azure OpenAI availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Completes a chat request using Azure OpenAI.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The LLM response.</returns>
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertMessages(request);
            var options = CreateChatOptions(request);

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            return ConvertResponse(response.Value, request.Model ?? _deploymentName);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI request failed with status {Status}", ex.Status);
            throw new InvalidOperationException($"Azure OpenAI request failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Streams a chat completion response from Azure OpenAI.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of stream responses.</returns>
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertMessages(request);
        var options = CreateChatOptions(request);

        AsyncCollectionResult<StreamingChatCompletionUpdate>? streamingResponse = null;
        string? errorMessage = null;

        try
        {
            streamingResponse = _chatClient.CompleteChatStreamingAsync(messages, options, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure OpenAI streaming request failed");
            errorMessage = ex.Message;
        }

        if (errorMessage != null)
        {
            yield return new LlmStreamResponse { Error = errorMessage };
            yield break;
        }

        if (streamingResponse == null)
        {
            yield break;
        }

        await using var enumerator = streamingResponse.GetAsyncEnumerator(cancellationToken);
        bool hasError = false;

        while (!hasError)
        {
            bool hasNext = false;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Azure OpenAI streaming cancelled");
                yield break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Azure OpenAI streaming");
                hasError = true;
                errorMessage = ex.Message;
            }

            if (hasError)
            {
                yield return new LlmStreamResponse { Error = errorMessage };
                yield break;
            }

            if (!hasNext)
            {
                break;
            }

            var update = enumerator.Current;

            // Handle content updates
            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    yield return new LlmStreamResponse
                    {
                        Delta = new Message { Role = Role.Assistant, Content = contentPart.Text },
                        IsComplete = false
                    };
                }
            }

            // Handle function calls
            if (update.ToolCallUpdates?.Count > 0)
            {
                foreach (var toolCall in update.ToolCallUpdates)
                {
                    if (toolCall.Kind == ChatToolCallKind.Function)
                    {
                        yield return new LlmStreamResponse
                        {
                            Delta = new Message
                            {
                                Role = Role.Assistant,
                                ToolCalls = new List<ToolCall>
                                {
                                    new ToolCall
                                    {
                                        Name = toolCall.FunctionName ?? string.Empty,
                                        Id = toolCall.ToolCallId ?? string.Empty,
                                        ArgumentsJson = toolCall.FunctionArgumentsUpdate?.ToString() ?? "{}"
                                    }
                                }
                            },
                            IsComplete = false
                        };
                    }
                }
            }

            // Check if complete
            if (update.FinishReason.HasValue)
            {
                yield return new LlmStreamResponse
                {
                    IsComplete = true,
                    FinishReason = update.FinishReason.ToString()
                };
            }
        }
    }

    /// <summary>
    /// Lists available models from Azure OpenAI.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of available models.</returns>
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        // Azure OpenAI doesn't provide a models listing endpoint
        // Instead, we return info about the configured deployment
        var models = new List<ModelInfo>
        {
            new ModelInfo
            {
                Id = _deploymentName,
                Name = _deploymentName,
                Provider = "azure",
                Description = $"Azure OpenAI deployment: {_deploymentName}",
                Family = GetDeploymentFamily(_deploymentName),
                ParameterSize = GetDeploymentParameterSize(_deploymentName),
                MaxTokens = GetDeploymentMaxTokens(_deploymentName),
                SupportsFunctions = true,
                SupportsVision = _deploymentName.Contains("vision") || _deploymentName.Contains("gpt-4o"),
                Metadata = new Dictionary<string, object>
                {
                    ["deployment"] = _deploymentName,
                    ["endpoint"] = _config.ApiBase ?? "Not specified"
                }
            }
        };

        return await Task.FromResult(models);
    }

    private static string? GetDeploymentFamily(string deploymentName)
    {
        return deploymentName switch
        {
            var name when name.Contains("gpt-4o") => "GPT-4o",
            var name when name.Contains("gpt-4") => "GPT-4",
            var name when name.Contains("gpt-35") || name.Contains("gpt-3.5") => "GPT-3.5",
            _ => null
        };
    }

    private static string? GetDeploymentParameterSize(string deploymentName)
    {
        // Azure doesn't expose parameter counts
        return deploymentName switch
        {
            var name when name.Contains("gpt-4") => "Large",
            var name when name.Contains("gpt-35") || name.Contains("gpt-3.5") => "Medium",
            _ => null
        };
    }

    private static int? GetDeploymentMaxTokens(string deploymentName)
    {
        return deploymentName switch
        {
            var name when name.Contains("gpt-4o") => 128000,
            var name when name.Contains("gpt-4-turbo") => 128000,
            var name when name.Contains("gpt-4-32k") => 32768,
            var name when name.Contains("gpt-4") && !name.Contains("32k") => 8192,
            var name when name.Contains("gpt-35-turbo-16k") || name.Contains("gpt-3.5-turbo-16k") => 16385,
            var name when name.Contains("gpt-35-turbo") || name.Contains("gpt-3.5-turbo") => 4096,
            _ => null
        };
    }

    private ProviderConfig LoadConfiguration(LlmOptions options)
    {
        if (options.Providers.TryGetValue("azure", out var config))
        {
            return config;
        }

        // Fall back to environment variables
        return new ProviderConfig
        {
            ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"),
            ApiBase = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"),
            DeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT"),
            ApiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION") ?? "2024-02-15-preview",
            Model = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? "gpt-4"
        };
    }

    private List<ChatMessage> ConvertMessages(LlmRequest request)
    {
        var messages = new List<ChatMessage>();

        // Add system message if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        // Convert messages
        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case Role.System:
                    var systemContent = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                    messages.Add(new SystemChatMessage(systemContent));
                    break;

                case Role.User:
                    var userContent = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                    messages.Add(new UserChatMessage(userContent));
                    break;

                case Role.Assistant:
                    var textParts = message.Parts.OfType<TextPart>().ToList();
                    var toolCalls = message.Parts.OfType<ToolCallPart>().ToList();

                    if (toolCalls.Any())
                    {
                        var assistantMessage = new AssistantChatMessage();

                        // Add text content if any
                        if (textParts.Any())
                        {
                            var text = string.Join(" ", textParts.Select(p => p.Text));
                            assistantMessage.Content.Add(ChatMessageContentPart.CreateTextPart(text));
                        }

                        // Add tool calls
                        foreach (var toolCallPart in toolCalls)
                        {
                            assistantMessage.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                toolCallPart.ToolCall.Id,
                                toolCallPart.ToolCall.Name,
                                BinaryData.FromString(toolCallPart.ToolCall.ArgumentsJson)));
                        }

                        messages.Add(assistantMessage);
                    }
                    else
                    {
                        // Simple text message
                        var text = string.Join(" ", textParts.Select(p => p.Text));
                        messages.Add(new AssistantChatMessage(text));
                    }
                    break;

                case Role.Tool:
                    var toolResponses = message.Parts.OfType<ToolResponsePart>().ToList();
                    foreach (var toolResponse in toolResponses)
                    {
                        messages.Add(new ToolChatMessage(
                            toolResponse.ToolResult.CallId,
                            toolResponse.ToolResult.ResultJson));
                    }
                    break;
            }
        }

        return messages;
    }

    private ChatCompletionOptions CreateChatOptions(LlmRequest request)
    {
        var options = new ChatCompletionOptions();

        options.Temperature = (float)request.Temperature;
        options.MaxOutputTokenCount = request.MaxTokens;

        // Add tools if provided
        if (request.Tools?.Any() == true)
        {
            foreach (var tool in request.Tools)
            {
                // Create function tool directly
                var parameters = BinaryData.FromObjectAsJson(tool.Parameters ?? new Dictionary<string, object>());
                options.Tools.Add(ChatTool.CreateFunctionTool(
                    tool.Name,
                    tool.Description ?? string.Empty,
                    parameters));
            }
        }

        return options;
    }

    private LlmResponse ConvertResponse(ChatCompletion completion, string model)
    {
        var content = string.Empty;
        var toolCalls = new List<ToolCall>();

        // Extract content
        foreach (var contentPart in completion.Content)
        {
            if (!string.IsNullOrEmpty(contentPart.Text))
            {
                content += contentPart.Text;
            }
        }

        // Extract tool calls
        if (completion.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in completion.ToolCalls)
            {
                if (toolCall.Kind == ChatToolCallKind.Function)
                {
                    toolCalls.Add(new ToolCall
                    {
                        Name = toolCall.FunctionName ?? "",
                        Id = toolCall.Id,
                        ArgumentsJson = toolCall.FunctionArguments?.ToString() ?? "{}"
                    });
                }
            }
        }

        return new LlmResponse
        {
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = content,
                ToolCalls = toolCalls
            },
            FinishReason = completion.FinishReason.ToString(),
            Usage = completion.Usage != null ? new LlmUsage
            {
                PromptTokens = completion.Usage.InputTokenCount,
                CompletionTokens = completion.Usage.OutputTokenCount,
                TotalTokens = completion.Usage.InputTokenCount + completion.Usage.OutputTokenCount
            } : null,
            Model = model
        };
    }
}
