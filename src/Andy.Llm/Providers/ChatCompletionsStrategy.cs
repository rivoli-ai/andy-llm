using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Andy.Llm.Configuration;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// Strategy implementation for the OpenAI Chat Completions API (/v1/chat/completions).
///
/// This is the traditional OpenAI API used by GPT-4, GPT-4o, and other standard models.
/// It sends a list of messages and receives a completion with optional tool calls.
/// </summary>
internal class ChatCompletionsStrategy : IOpenAIApiStrategy
{
    private readonly ChatClient _chatClient;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public string ApiType => "chat-completions";

    /// <summary>
    /// Creates a new Chat Completions strategy.
    /// </summary>
    /// <param name="chatClient">The OpenAI SDK chat client configured for the target model.</param>
    /// <param name="logger">Logger instance.</param>
    public ChatCompletionsStrategy(ChatClient chatClient, ILogger logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertMessages(request);
            var options = CreateCompletionOptions(request);

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            if (response?.Value == null)
            {
                _logger.LogError("OpenAI Chat Completions API returned null response");
                return new LlmResponse
                {
                    AssistantMessage = new Message { Role = Role.Assistant, Content = "OpenAI API returned null response" },
                    FinishReason = "error"
                };
            }

            var completion = response.Value;
            var functionCalls = ExtractFunctionCalls(completion);

            // Safely extract text content
            var content = "";
            if (completion.Content != null && completion.Content.Count > 0)
            {
                var firstContent = completion.Content[0];
                if (firstContent != null)
                {
                    content = firstContent.Text ?? "";
                }
            }

            var finishReason = completion.FinishReason.ToString();

            // Detailed logging for debugging
            var firstText = (completion.Content != null && completion.Content.Count > 0 && completion.Content[0] != null)
                ? completion.Content[0].Text ?? "null"
                : "empty";
            _logger.LogInformation("OpenAI RAW response - Content.Count: {ContentCount}, Content[0]?.Text: '{Text}', FinishReason: '{Reason}', ToolCalls: {ToolCount}",
                completion.Content?.Count ?? 0,
                firstText,
                finishReason,
                functionCalls.Count);

            // If no content and no tool calls, this is likely an error
            if (string.IsNullOrEmpty(content) && functionCalls.Count == 0)
            {
                var errorMsg = $"OpenAI returned no content. FinishReason: {finishReason}";
                _logger.LogWarning(errorMsg);
                content = errorMsg;
            }

            _logger.LogInformation("OpenAI final content to return: '{Content}'", content);

            return new LlmResponse
            {
                AssistantMessage = new Message
                {
                    Role = Role.Assistant,
                    Content = content,
                    ToolCalls = functionCalls
                },
                FinishReason = finishReason,
                Usage = completion.Usage != null ? new LlmUsage
                {
                    PromptTokens = completion.Usage.InputTokenCount,
                    CompletionTokens = completion.Usage.OutputTokenCount,
                    TotalTokens = completion.Usage.TotalTokenCount
                } : null,
                Model = completion.Model
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenAI Chat Completions: {Message}", ex.Message);
            return new LlmResponse
            {
                AssistantMessage = new Message { Role = Role.Assistant, Content = $"OpenAI Error: {ex.Message}" },
                FinishReason = "error"
            };
        }
    }

    /// <inheritdoc />
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
                        Delta = new Message { Role = Role.Assistant, Content = text },
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
                            var partialCall = new ToolCall
                            {
                                Id = string.IsNullOrEmpty(accumulated.Id) ? $"partial_{index}" : accumulated.Id,
                                Name = accumulated.Name,
                                ArgumentsJson = accumulated.Arguments ?? "{}"
                            };

                            yield return new LlmStreamResponse
                            {
                                Delta = new Message
                                {
                                    Role = Role.Assistant,
                                    ToolCalls = new List<ToolCall> { partialCall }
                                },
                                IsComplete = false
                            };
                        }
                    }

                    // Check if tool call is complete
                    if (IsToolCallComplete(accumulated))
                    {
                        var functionCall = new ToolCall
                        {
                            Id = accumulated.Id ?? $"call_{Guid.NewGuid():N}"[..8],
                            Name = accumulated.Name,
                            ArgumentsJson = accumulated.Arguments ?? "{}"
                        };

                        yield return new LlmStreamResponse
                        {
                            Delta = new Message
                            {
                                Role = Role.Assistant,
                                ToolCalls = new List<ToolCall> { functionCall }
                            },
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
                        var functionCall = new ToolCall
                        {
                            Id = accumulated.Id ?? $"call_{Guid.NewGuid():N}"[..8],
                            Name = accumulated.Name,
                            ArgumentsJson = accumulated.Arguments ?? "{}"
                        };

                        yield return new LlmStreamResponse
                        {
                            Delta = new Message
                            {
                                Role = Role.Assistant,
                                ToolCalls = new List<ToolCall> { functionCall }
                            },
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

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
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
            _logger.LogWarning(ex, "Chat Completions availability check failed");
            return false;
        }
    }

    #region Message Conversion

    internal static List<ChatMessage> ConvertMessages(LlmRequest request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new SystemChatMessage(request.SystemPrompt));
        }

        foreach (var message in request.Messages)
        {
            messages.AddRange(ConvertMessage(message));
        }

        return messages;
    }

    internal static IEnumerable<ChatMessage> ConvertMessage(Message message)
    {
        switch (message.Role)
        {
            case Role.System:
                var systemText = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                yield return new SystemChatMessage(systemText);
                break;

            case Role.User:
                var userText = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));
                yield return new UserChatMessage(userText);
                break;

            case Role.Assistant:
                var textParts = message.Parts.OfType<TextPart>().ToList();
                var toolCallParts = message.Parts.OfType<ToolCallPart>().ToList();

                if (toolCallParts.Any())
                {
                    var toolCalls = toolCallParts.Select(tcp => ChatToolCall.CreateFunctionToolCall(
                        tcp.ToolCall.Id,
                        tcp.ToolCall.Name,
                        BinaryData.FromString(tcp.ToolCall.ArgumentsJson)
                    )).ToList();

                    var assistantText = textParts.Any()
                        ? string.Join(" ", textParts.Select(p => p.Text))
                        : "";
                    var assistantMessage = new AssistantChatMessage(assistantText);
                    foreach (var tc in toolCalls)
                    {
                        assistantMessage.ToolCalls.Add(tc);
                    }
                    yield return assistantMessage;
                }
                else if (textParts.Any())
                {
                    yield return new AssistantChatMessage(string.Join(" ", textParts.Select(p => p.Text)));
                }
                break;

            case Role.Tool:
                if (!string.IsNullOrEmpty(message.ToolCallId))
                {
                    yield return new ToolChatMessage(message.ToolCallId, message.Content);
                }
                else
                {
                    foreach (var part in message.Parts.OfType<ToolResponsePart>())
                    {
                        yield return new ToolChatMessage(part.ToolResult.CallId, part.ToolResult.ResultJson);
                    }
                }
                break;
        }
    }

    #endregion

    #region Options & Extraction

    internal static ChatCompletionOptions CreateCompletionOptions(LlmRequest request)
    {
        var options = new ChatCompletionOptions
        {
            Temperature = (float?)request.Temperature,
            MaxOutputTokenCount = request.MaxTokens
        };

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

    internal static List<ToolCall> ExtractFunctionCalls(ChatCompletion completion)
    {
        var functionCalls = new List<ToolCall>();

        if (completion.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in completion.ToolCalls)
            {
                functionCalls.Add(new ToolCall
                {
                    Id = toolCall.Id,
                    Name = toolCall.FunctionName ?? "",
                    ArgumentsJson = toolCall.FunctionArguments?.ToString() ?? "{}"
                });
            }
        }

        return functionCalls;
    }

    #endregion

    #region Helpers

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

    #endregion
}
