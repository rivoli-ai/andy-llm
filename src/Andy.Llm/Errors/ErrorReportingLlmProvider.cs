using System.Runtime.CompilerServices;
using Andy.Model.Llm;

namespace Andy.Llm.Errors;

/// <summary>Normalizes provider failures while preserving cancellation and streaming fallback.</summary>
public sealed class ErrorReportingLlmProvider(ILlmProvider inner) : ILlmProvider
{
    /// <inheritdoc />
    public string Name => inner.Name ?? "unknown";
    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => inner.IsAvailableAsync(cancellationToken);
    /// <inheritdoc />
    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => inner.ListModelsAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        { return await inner.CompleteAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw LlmProviderException.FromException(Name, ex); }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<LlmStreamResponse> iterator;
        try
        { iterator = inner.StreamCompleteAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (NotSupportedException) { throw; }
        catch (Exception ex) { throw LlmProviderException.FromException(Name, ex); }
        await using (iterator)
        {
            while (true)
            {
                bool more;
                try
                { more = await iterator.MoveNextAsync().ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (NotSupportedException) { throw; }
                catch (Exception ex) { throw LlmProviderException.FromException(Name, ex); }
                if (!more)
                {
                    break;
                }

                var chunk = iterator.Current;
                if (!string.IsNullOrEmpty(chunk.Error))
                {
                    throw LlmProviderException.FromMessage(Name, chunk.Error);
                }

                yield return chunk;
            }
        }
    }
}
