using System.Runtime.CompilerServices;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
using Andy.Llm.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Andy.Llm;

/// <summary>
/// A client for interacting with Large Language Models through various providers.
/// </summary>
public class LlmClient
{
    private readonly OpenAIClient? _openAiClient;
    private readonly ILlmProvider? _provider;
    private readonly ILlmProviderFactory? _providerFactory;
    private readonly ILogger<LlmClient>? _logger;

    /// <summary>
    /// Initializes a new instance of the LlmClient with the specified API key.
    /// </summary>
    /// <param name="apiKey">The OpenAI API key.</param>
    public LlmClient(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey));
        }

        _openAiClient = new OpenAIClient(apiKey);
    }

    /// <summary>
    /// Initializes a new instance of the LlmClient with an existing OpenAI client.
    /// </summary>
    /// <param name="openAiClient">The OpenAI client to use.</param>
    public LlmClient(OpenAIClient openAiClient)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmClient"/> class using a provider factory.
    /// </summary>
    /// <param name="providerFactory">The provider factory.</param>
    /// <param name="logger">The logger.</param>
    public LlmClient(ILlmProviderFactory providerFactory, ILogger<LlmClient> logger)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmClient"/> class using a specific provider.
    /// </summary>
    /// <param name="provider">The LLM provider.</param>
    /// <param name="logger">The logger.</param>
    public LlmClient(ILlmProvider provider, ILogger<LlmClient> logger)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a chat completion request to the language model.
    /// </summary>
    /// <param name="messages">The messages to send.</param>
    /// <param name="model">The model to use (defaults to gpt-4).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chat completion response.</returns>
    public virtual async Task<ChatCompletion> GetChatCompletionAsync(
        IEnumerable<ChatMessage> messages,
        string model = "gpt-4",
        CancellationToken cancellationToken = default)
    {
        if (_openAiClient != null)
        {
            var chatClient = _openAiClient.GetChatClient(model);
            return await chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
        }

        // Convert to new API format
        var request = new LlmRequest
        {
            Messages = ConvertFromChatMessages(messages),
            Model = model,
            Stream = false
        };

        var provider = await GetProviderAsync(cancellationToken);
        var response = await provider.CompleteAsync(request, cancellationToken);

        // Convert response back to ChatCompletion format
        // This is a simplified conversion for compatibility
        throw new NotImplementedException("Legacy compatibility mode not fully implemented. Use the new CompleteAsync method instead.");
    }

    /// <summary>
    /// Streams a chat completion request to the language model.
    /// </summary>
    /// <param name="messages">The messages to send.</param>
    /// <param name="model">The model to use (defaults to gpt-4).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of streaming updates.</returns>
    public virtual async IAsyncEnumerable<StreamingChatCompletionUpdate> GetChatCompletionStreamAsync(
        IEnumerable<ChatMessage> messages,
        string model = "gpt-4",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_openAiClient != null)
        {
            var chatClient = _openAiClient.GetChatClient(model);
            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken))
            {
                yield return update;
            }
        }
        else
        {
            throw new NotImplementedException("Legacy streaming compatibility mode not implemented. Use the new StreamCompleteAsync method instead.");
        }
    }

    /// <summary>
    /// Sends a simple text message and returns the response content.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <param name="model">The model to use (defaults to gpt-4).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response content as a string.</returns>
    public virtual async Task<string> GetResponseAsync(
        string message,
        string model = "gpt-4",
        CancellationToken cancellationToken = default)
    {
        if (_openAiClient != null)
        {
            var messages = new[] { new UserChatMessage(message) };
            var completion = await GetChatCompletionAsync(messages, model, cancellationToken);
            return completion.Content[0].Text;
        }

        // Use new API
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, message)
            },
            Model = model,
            Stream = false
        };

        var provider = await GetProviderAsync(cancellationToken);
        var response = await provider.CompleteAsync(request, cancellationToken);
        return response.Content;
    }

    /// <summary>
    /// Completes a chat request using the new API
    /// </summary>
    public virtual async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(cancellationToken);
        return await provider.CompleteAsync(request, cancellationToken);
    }

    /// <summary>
    /// Completes a chat request with streaming response using the new API
    /// </summary>
    public virtual async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var provider = await GetProviderAsync(cancellationToken);
        await foreach (var chunk in provider.StreamCompleteAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Gets a chat client for direct OpenAI SDK usage (legacy compatibility)
    /// </summary>
    public virtual ChatClient? GetChatClient(string model = "gpt-4o")
    {
        return _openAiClient?.GetChatClient(model);
    }

    private async Task<ILlmProvider> GetProviderAsync(CancellationToken cancellationToken)
    {
        if (_provider != null)
        {
            return _provider;
        }

        if (_providerFactory != null)
        {
            return await _providerFactory.CreateAvailableProviderAsync(cancellationToken);
        }

        // Legacy compatibility - create a provider from the OpenAI client
        if (_openAiClient != null)
        {
            // For backward compatibility, we'll create a simple provider wrapper
            // In production, this should be handled through proper DI
            throw new NotSupportedException("Legacy OpenAIClient mode requires provider factory. Use the new constructor overloads or migrate to the provider-based API.");
        }

        throw new InvalidOperationException("No provider available");
    }

    private static List<Message> ConvertFromChatMessages(IEnumerable<ChatMessage> chatMessages)
    {
        var messages = new List<Message>();

        foreach (var chatMessage in chatMessages)
        {
            var role = chatMessage switch
            {
                SystemChatMessage => MessageRole.System,
                UserChatMessage => MessageRole.User,
                AssistantChatMessage => MessageRole.Assistant,
                ToolChatMessage => MessageRole.Tool,
                _ => MessageRole.User
            };

            if (chatMessage.Content?.Count > 0)
            {
                var text = string.Join("", chatMessage.Content.Select(c => c.Text));
                messages.Add(Message.CreateText(role, text));
            }
        }

        return messages;
    }
}
