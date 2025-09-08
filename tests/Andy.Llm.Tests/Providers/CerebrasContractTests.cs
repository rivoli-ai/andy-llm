using Xunit;
using Andy.Llm.Models;
using Andy.Llm.Abstractions;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Tests.Providers;

internal class StubStreamProvider : ILlmProvider
{
    public string Name => "Stub";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse());

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new LlmStreamResponse
        {
            FunctionCall = new FunctionCall { Id = "partial_0", Name = "tool", Arguments = new(), ArgumentsJson = "{\"x\":1}" },
            IsComplete = false
        };
        await Task.Delay(1, cancellationToken);
        yield return new LlmStreamResponse
        {
            FunctionCall = new FunctionCall { Id = "call_1", Name = "tool", Arguments = new(), ArgumentsJson = "{\"x\":1}" },
            IsComplete = false
        };
        yield return new LlmStreamResponse { IsComplete = true, FinishReason = "stop" };
    }

    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<ModelInfo>>(Array.Empty<ModelInfo>());
}

public class CerebrasContractTests
{
    [Fact]
    public async Task StreamCompleteAsync_ShouldEmitPartialAndFinal_WithFinishReason()
    {
        var provider = new StubStreamProvider();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<Andy.Llm.LlmClient>();
        var client = new Andy.Llm.LlmClient(provider, logger);

        var req = new LlmRequest { Messages = new List<Message> { Message.CreateText(MessageRole.User, "hi") } };

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(req))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.False(chunks[0].IsComplete);
        Assert.NotNull(chunks[0].FunctionCall?.ArgumentsJson);
        Assert.Equal("stop", chunks[2].FinishReason);
        Assert.True(chunks[2].IsComplete);
    }
}
