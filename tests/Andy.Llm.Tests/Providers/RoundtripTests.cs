using Xunit;
using Andy.Llm.Models;
using Andy.Llm.Abstractions;
using Andy.Context.Model;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Tests.Providers;

internal class StubRoundtripProvider : ILlmProvider
{
    public string Name => "Stub";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        // Return mixed text + multiple tool calls
        return Task.FromResult(new LlmResponse
        {
            Content = "I'll call two tools.",
            FunctionCalls = new List<FunctionCall>
            {
                new FunctionCall { Id = "call_1", Name = "get_weather", Arguments = new Dictionary<string, object?> { ["location"] = "NYC" }, ArgumentsJson = "{\"location\":\"NYC\"}" },
                new FunctionCall { Id = "call_2", Name = "get_time", Arguments = new Dictionary<string, object?> { ["timezone"] = "EST" }, ArgumentsJson = "{\"timezone\":\"EST\"}" }
            },
            FinishReason = "tool_calls"
        });
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Emit text delta
        yield return new LlmStreamResponse { TextDelta = "I will ", IsComplete = false };
        yield return new LlmStreamResponse { TextDelta = "call ", IsComplete = false };
        yield return new LlmStreamResponse { TextDelta = "tools.", IsComplete = false };
        // Emit first tool
        yield return new LlmStreamResponse { FunctionCall = new FunctionCall { Id = "partial_0", Name = "get_weather", Arguments = new(), ArgumentsJson = "{\"location\":\"NY\"}" }, IsComplete = false };
        yield return new LlmStreamResponse { FunctionCall = new FunctionCall { Id = "call_1", Name = "get_weather", Arguments = new(), ArgumentsJson = "{\"location\":\"NYC\"}" }, IsComplete = false };
        // Emit second tool
        yield return new LlmStreamResponse { FunctionCall = new FunctionCall { Id = "call_2", Name = "get_time", Arguments = new(), ArgumentsJson = "{\"timezone\":\"EST\"}" }, IsComplete = false };
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
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<Andy.Llm.LlmClient>();
        var client = new Andy.Llm.LlmClient(provider, logger);

        var req = new LlmRequest { Messages = new List<Message> { new Message { Role = Role.User, Content = "test" } } };
        var res = await client.CompleteAsync(req);

        Assert.Equal("I'll call two tools.", res.Content);
        Assert.Equal(2, res.FunctionCalls.Count);
        Assert.Equal("get_weather", res.FunctionCalls[0].Name);
        Assert.Equal("{\"location\":\"NYC\"}", res.FunctionCalls[0].ArgumentsJson);
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
