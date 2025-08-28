using System.ClientModel;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// Provider for Azure OpenAI Service.
/// </summary>
public class AzureOpenAIProvider : ILlmProvider
{
    private readonly ILogger<AzureOpenAIProvider> _logger;
    private readonly ProviderConfig _config;
    private readonly AzureOpenAIClient _azureClient;
    private readonly ChatClient _chatClient;
    private readonly string _deploymentName;

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

    public string Name => "azure";

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
            yield break;

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
                break;
            
            var update = enumerator.Current;
            
            // Handle content updates
            foreach (var contentPart in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(contentPart.Text))
                {
                    yield return new LlmStreamResponse
                    {
                        TextDelta = contentPart.Text,
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
                            FunctionCall = new FunctionCall
                            {
                                Name = toolCall.FunctionName ?? string.Empty,
                                Id = toolCall.ToolCallId ?? string.Empty,
                                Arguments = new Dictionary<string, object?>
                                {
                                    ["arguments_json"] = toolCall.FunctionArgumentsUpdate?.ToString()
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
                    IsComplete = true
                };
            }
        }
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
                case MessageRole.System:
                    var systemContent = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                    messages.Add(new SystemChatMessage(systemContent));
                    break;
                    
                case MessageRole.User:
                    var userContent = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                    messages.Add(new UserChatMessage(userContent));
                    break;
                    
                case MessageRole.Assistant:
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
                        foreach (var toolCall in toolCalls)
                        {
                            assistantMessage.ToolCalls.Add(ChatToolCall.CreateFunctionToolCall(
                                toolCall.CallId,
                                toolCall.ToolName,
                                BinaryData.FromObjectAsJson(toolCall.Arguments)));
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
                    
                case MessageRole.Tool:
                    var toolResponses = message.Parts.OfType<ToolResponsePart>().ToList();
                    foreach (var toolResponse in toolResponses)
                    {
                        var toolContent = System.Text.Json.JsonSerializer.Serialize(toolResponse.Response);
                        messages.Add(new ToolChatMessage(
                            toolResponse.CallId,
                            toolContent));
                    }
                    break;
            }
        }
        
        return messages;
    }

    private ChatCompletionOptions CreateChatOptions(LlmRequest request)
    {
        var options = new ChatCompletionOptions();
        
        if (request.Temperature.HasValue)
            options.Temperature = (float)request.Temperature.Value;
            
        if (request.MaxTokens.HasValue)
            options.MaxOutputTokenCount = request.MaxTokens.Value;
        
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
        var response = new LlmResponse
        {
            Content = string.Empty,
            Model = model,
            FunctionCalls = new List<FunctionCall>()
        };
        
        // Extract content and function calls
        foreach (var contentPart in completion.Content)
        {
            if (!string.IsNullOrEmpty(contentPart.Text))
            {
                response.Content += contentPart.Text;
            }
        }
        
        // Extract function calls
        if (completion.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in completion.ToolCalls)
            {
                if (toolCall.Kind == ChatToolCallKind.Function)
                {
                    response.FunctionCalls.Add(new FunctionCall
                    {
                        Name = toolCall.FunctionName,
                        Id = toolCall.Id,
                        Arguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
                            toolCall.FunctionArguments?.ToString() ?? "{}") ?? new Dictionary<string, object?>()
                    });
                }
            }
        }
        
        // Add usage information
        if (completion.Usage != null)
        {
            var inputTokens = completion.Usage.InputTokenCount;
            var outputTokens = completion.Usage.OutputTokenCount;
            var totalTokens = inputTokens + outputTokens;
            response.TokensUsed = totalTokens;
            response.Usage = new TokenUsage
            {
                PromptTokens = inputTokens,
                CompletionTokens = outputTokens,
                TotalTokens = totalTokens
            };
        }
        
        response.FinishReason = completion.FinishReason.ToString();
        
        return response;
    }
}