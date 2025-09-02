using System.Runtime.CompilerServices;
using Andy.Llm.Models;

namespace Andy.Llm.Abstractions;

/// <summary>
/// Interface for LLM providers supporting OpenAI-compatible APIs
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Completes a chat request and returns the full response
    /// </summary>
    Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a chat request and returns a stream of response chunks
    /// </summary>
    IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the provider name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Validates if the provider is properly configured and available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists available models from the provider
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of available models.</returns>
    Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);
}