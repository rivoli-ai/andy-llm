using System.Net;
using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using System.Text.Json;
using System.Text;
using Andy.Llm.Configuration;
using Andy.Llm.Errors;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Llm.Tests.Providers;

public class KeepAliveResponseTests
{
    private const string Completion = """{"id":"gen-1","object":"chat.completion","created":1,"model":"test","choices":[{"index":0,"message":{"role":"assistant","content":"hello : world"},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":2,"total_tokens":6}}""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeadingCommentsPreserveCompletion(bool dedicated)
    {
        using var handler = new Handler("\r\n: OPENROUTER PROCESSING\r\n:\r\n\r\n" + Completion);
        var result = await Build(dedicated, handler).CompleteAsync(Request());
        Assert.Equal("hello : world", result.Content);
        Assert.Equal("test", result.Model);
        Assert.Equal(6, result.Usage!.TotalTokens);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, " \r\n\t")]
    [InlineData(true, " \r\n\t")]
    [InlineData(false, ": OPENROUTER PROCESSING\n")]
    [InlineData(true, ": OPENROUTER PROCESSING\n")]
    [InlineData(false, ": OPENROUTER PROCESSING")]
    [InlineData(true, ": OPENROUTER PROCESSING")]
    public async Task EmptyOrCommentOnlyBodyIsRetryableTimeout(bool dedicated, string body)
    {
        using var handler = new Handler(body);
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => Build(dedicated, handler).CompleteAsync(Request()));
        Assert.Equal(504, error.Error.StatusCode);
        Assert.Equal("openrouter/test", error.Error.Provider);
        Assert.Contains("retry", error.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlainJsonStillWorks(bool dedicated)
    {
        using var handler = new Handler(Completion);
        Assert.Equal("hello : world", (await Build(dedicated, handler).CompleteAsync(Request())).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongCommentOnlyResponseIsTimeout(bool dedicated)
    {
        using var handler = new Handler(string.Concat(Enumerable.Repeat(": OPENROUTER PROCESSING\n", 168)));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => Build(dedicated, handler).CompleteAsync(Request()));
        Assert.Equal(504, error.Error.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommentsBeforeToolCallsPreserveArguments(bool dedicated)
    {
        const string body = """{"id":"gen-1","object":"chat.completion","created":1,"model":"test","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"lookup","arguments":"{\"key\":\"a:b\"}"}}]},"finish_reason":"tool_calls"}]}""";
        using var handler = new Handler(": processing\n" + body);
        var result = await Build(dedicated, handler).CompleteAsync(Request());
        var tool = Assert.Single(result.AssistantMessage!.ToolCalls!);
        Assert.Equal("call_1", tool.Id);
        Assert.Equal("lookup", tool.Name);
        using var arguments = JsonDocument.Parse(tool.ArgumentsJson!);
        Assert.Equal("a:b", arguments.RootElement.GetProperty("key").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedJsonIsNotMisclassifiedAsTimeout(bool dedicated)
    {
        using var handler = new Handler(": processing\n{broken");
        var error = await Record.ExceptionAsync(() => Build(dedicated, handler).CompleteAsync(Request()));
        Assert.NotNull(error);
        Assert.IsNotType<LlmProviderException>(error);
        Assert.True(error is JsonException || error.InnerException is JsonException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpErrorsKeepTheirActualStatus(bool dedicated)
    {
        using var handler = new Handler("", HttpStatusCode.Unauthorized);
        var error = await Record.ExceptionAsync(() => Build(dedicated, handler).CompleteAsync(Request()));
        if (dedicated)
        {
            Assert.Equal(401, Assert.IsType<LlmProviderException>(error).Error.StatusCode);
        }
        else
        {
            Assert.Equal(401, Assert.IsType<ClientResultException>(error!.InnerException).Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingCommentsRemainSupported(bool dedicated)
    {
        const string body = """{"id":"gen-1","object":"chat.completion.chunk","created":1,"model":"test","choices":[{"index":0,"delta":{"role":"assistant","content":"hello"},"finish_reason":null}]}""";
        using var handler = new Handler(": OPENROUTER PROCESSING\n\ndata: " + body + "\n\ndata: [DONE]\n\n");
        var content = new StringBuilder();
        await foreach (var chunk in Build(dedicated, handler).StreamCompleteAsync(Request()))
        {
            content.Append(chunk.Delta?.Content);
        }
        Assert.Equal("hello", content.ToString());
    }

    private static LlmRequest Request() => new() { Messages = [new Message { Role = Role.User, Content = "hi" }] };

    private static IOpenAIApiStrategy Build(bool dedicated, Handler handler)
    {
        var config = new ProviderConfig { ApiBase = "https://openrouter.ai/api/v1", ApiKey = "test", Model = "test" };
        var factory = new Factory(handler);
        if (dedicated)
        {
            return new DedicatedStrategy(new OpenRouterProvider(config, "openrouter/test", NullLogger<OpenRouterProvider>.Instance, factory));
        }

        // Exercise the real SDK and strategy with the same policy installed by OpenAIProvider.
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.ApiBase),
            Transport = new HttpClientPipelineTransport(factory.CreateClient("test"))
        };
        options.AddPolicy(new KeepAliveResponsePolicy("openrouter/test"), PipelinePosition.PerCall);
        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), options).GetChatClient(config.Model);
        return new ChatCompletionsStrategy(client, NullLogger.Instance);
    }

    private sealed class DedicatedStrategy(OpenRouterProvider provider) : IOpenAIApiStrategy
    {
        public string ApiType => "chat-completions";
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
            => provider.CompleteAsync(request, cancellationToken);
        public IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
            => provider.StreamCompleteAsync(request, cancellationToken);
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => provider.IsAvailableAsync(cancellationToken);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
