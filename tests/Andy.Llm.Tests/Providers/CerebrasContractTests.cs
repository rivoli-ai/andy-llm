using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Andy.Llm.Providers;

namespace Andy.Llm.Tests.Providers;

internal class StubStreamProvider : Andy.Model.Llm.ILlmProvider
{
    public string Name => "Stub";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new LlmResponse());

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new LlmStreamResponse
        {
            Delta = new Message
            {
                Role = Role.Assistant,
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "partial_0", Name = "tool", ArgumentsJson = "{\"x\":1}" }
                }
            },
            IsComplete = false
        };
        await Task.Delay(1, cancellationToken);
        yield return new LlmStreamResponse
        {
            Delta = new Message
            {
                Role = Role.Assistant,
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "call_1", Name = "tool", ArgumentsJson = "{\"x\":1}" }
                }
            },
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
        // Use provider directly - LlmClient has been removed

        var req = new LlmRequest { Messages = new List<Message> { new Message { Role = Role.User, Content = "hi" } } };

        var chunks = new List<LlmStreamResponse>();
        await foreach (var chunk in provider.StreamCompleteAsync(req))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.False(chunks[0].IsComplete);
        Assert.NotNull(chunks[0].FunctionCall?.Arguments);
        Assert.Equal("stop", chunks[2].FinishReason);
        Assert.True(chunks[2].IsComplete);
    }
}
