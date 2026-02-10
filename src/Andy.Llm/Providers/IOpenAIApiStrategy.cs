using Andy.Model.Llm;

namespace Andy.Llm.Providers;

/// <summary>
/// Strategy interface for OpenAI API protocol variants.
///
/// OpenAI provides multiple API endpoints with different protocols:
/// - Chat Completions API (/v1/chat/completions): Traditional chat-based completion
/// - Responses API (/v1/responses): Newer API required for Codex models, supports
///   built-in tools, reasoning persistence, and background execution
///
/// Each strategy implements the same logical operations using the appropriate
/// wire format for its API endpoint.
/// </summary>
internal interface IOpenAIApiStrategy
{
    /// <summary>
    /// Gets the API type identifier for this strategy (e.g., "chat-completions", "responses").
    /// </summary>
    string ApiType { get; }

    /// <summary>
    /// Completes a request and returns the full response.
    /// </summary>
    /// <param name="request">The LLM request with messages, tools, and config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete LLM response.</returns>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a completion response as a series of chunks.
    /// </summary>
    /// <param name="request">The LLM request with messages, tools, and config.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async stream of response chunks.</returns>
    IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the API endpoint is available and responding.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the endpoint is available.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
