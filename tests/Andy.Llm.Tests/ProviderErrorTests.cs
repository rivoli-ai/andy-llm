using System.Net;
using System.Runtime.CompilerServices;
using Andy.Llm.Errors;
using Andy.Model.Llm;
using Moq;
using Xunit;

namespace Andy.Llm.Tests;

public class ProviderErrorTests
{
    [Fact]
    public void MalformedBodyDoesNotExposeEnvelopeOrLoseHttpStatus()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var failure = LlmProviderException.FromHttpResponse("gateway", response, "{ broken private payload");
        Assert.Equal(502, failure.Error.StatusCode);
        Assert.Equal("Provider request failed.", failure.Error.Message);
        Assert.Null(failure.Error.RetryAfterSeconds);
    }

    [Fact]
    public async Task CompleteNormalizesHttpFailuresAndPreservesCancellation()
    {
        var inner = new Mock<ILlmProvider>();
        inner.SetupGet(p => p.Name).Returns("openai");
        inner.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable));
        var wrapper = new ErrorReportingLlmProvider(inner.Object);
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => wrapper.CompleteAsync(new() { Messages = [] }));
        Assert.Equal("openai", error.Error.Provider);
        Assert.Equal(503, error.Error.StatusCode);
        inner.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wrapper.CompleteAsync(new() { Messages = [] }));
    }

    [Fact]
    public async Task StreamErrorPacketsBecomeTypedFailures()
    {
        var inner = new Mock<ILlmProvider>();
        inner.SetupGet(p => p.Name).Returns("test");
        inner.Setup(p => p.StreamCompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Returns(ErrorStream());
        var error = await Assert.ThrowsAsync<LlmProviderException>(async () =>
        {
            await foreach (var chunk in new ErrorReportingLlmProvider(inner.Object).StreamCompleteAsync(new() { Messages = [] }))
            { }
        });
        Assert.Equal(429, error.Error.StatusCode);
        Assert.Equal("slow down", error.Error.Message);
    }

    [Fact]
    public async Task UnsupportedStreamingRetainsFallbackSignal()
    {
        var inner = new Mock<ILlmProvider>();
        inner.Setup(p => p.StreamCompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>())).Throws(new NotSupportedException());
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var chunk in new ErrorReportingLlmProvider(inner.Object).StreamCompleteAsync(new() { Messages = [] }))
            { }
        });
    }

    [Fact]
    public void MessagesAreBoundedAndDoNotContainTerminalControlsOrApiKeys()
    {
        var error = LlmProviderException.FromMessage("provider", "sk-secret123456789\u001b[31m " + new string('x', 5000));
        Assert.DoesNotContain("sk-secret123456789", error.Message);
        Assert.DoesNotContain('\u001b', error.Message);
        Assert.InRange(error.Error.Message.Length, 1, 2003);
    }

    private static async IAsyncEnumerable<LlmStreamResponse> ErrorStream([EnumeratorCancellation] CancellationToken token = default)
    {
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        yield return new LlmStreamResponse { Error = """{"error":{"code":429,"message":"slow down"}}""", IsComplete = true };
    }
}
