using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Andy.Llm.Providers;

namespace Andy.Llm.Tests.Providers;

internal class StubRoundtripProvider : Andy.Llm.Providers.ILlmProvider
{
    public string Name => "Stub";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        // Return mixed text + multiple tool calls
        return Task.FromResult(new LlmResponse
        {
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = "I'll call two tools.",
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "call_1", Name = "get_weather", ArgumentsJson = "{\"location\":\"NYC\"}" },
                    new ToolCall { Id = "call_2", Name = "get_time", ArgumentsJson = "{\"timezone\":\"EST\"}" }
                }
            },
            FinishReason = "tool_calls"
        });
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Emit text delta
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, Content = "I will " }, IsComplete = false };
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, Content = "call " }, IsComplete = false };
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, Content = "tools." }, IsComplete = false };
        // Emit first tool
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, ToolCalls = new List<ToolCall> { new ToolCall { Id = "partial_0", Name = "get_weather", ArgumentsJson = "{\"location\":\"NY\"}" } } }, IsComplete = false };
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, ToolCalls = new List<ToolCall> { new ToolCall { Id = "call_1", Name = "get_weather", ArgumentsJson = "{\"location\":\"NYC\"}" } } }, IsComplete = false };
        // Emit second tool
        yield return new LlmStreamResponse { Delta = new Message { Role = Role.Assistant, ToolCalls = new List<ToolCall> { new ToolCall { Id = "call_2", Name = "get_time", ArgumentsJson = "{\"timezone\":\"EST\"}" } } }, IsComplete = false };
        // Complete
        yield return new LlmStreamResponse { IsComplete = true, FinishReason = "stop" };
        await Task.CompletedTask;
    }

    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<ModelInfo>>(Array.Empty<ModelInfo>());
}

public class RoundtripTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnMixedText_AndMultipleToolCalls()
    {
        var provider = new StubRoundtripProvider();
        // Use provider directly - LlmClient has been removed

        var req = new LlmRequest { Messages = new List<Message> { new Message { Role = Role.User, Content = "test" } } };
        var res = await provider.CompleteAsync(req);

        Assert.Equal("I'll call two tools.", res.Content);
        Assert.Equal(2, res.FunctionCalls.Count);
        Assert.Equal("get_weather", res.FunctionCalls[0].Name);
        Assert.Equal("{\"location\":\"NYC\"}", res.FunctionCalls[0].Arguments);
        Assert.Equal("get_time", res.FunctionCalls[1].Name);
    }

    [Fact]
    public async Task StreamCompleteAsync_ShouldReturnTextDeltas_ToolCalls_AndFinishReason()
    {
        var provider = new StubRoundtripProvider();
        var req = new LlmRequest { Messages = new List<Message> { new Message { Role = Role.User, Content = "stream" } } };

        var chunks = new List<LlmStreamResponse>();
        await foreach (var c in provider.StreamCompleteAsync(req))
            chunks.Add(c);

        Assert.Contains(chunks, c => c.TextDelta != null);
        Assert.Contains(chunks, c => c.FunctionCall != null);
        Assert.True(chunks.Last().IsComplete);
        Assert.Equal("stop", chunks.Last().FinishReason);
    }
}
