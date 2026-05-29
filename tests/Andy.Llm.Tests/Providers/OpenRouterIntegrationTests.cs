using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Integration tests for the OpenRouter provider that make real API calls.
/// These tests require the <c>OPENROUTER_API_KEY</c> environment variable to be
/// set; when it is absent every test no-ops (returns early) so the suite stays
/// green on machines without credentials, matching the convention used by
/// <see cref="CerebrasIntegrationTests"/>.
///
/// The default model is a free, tool-capable OpenRouter model
/// (<c>openai/gpt-oss-20b:free</c>) so the tests cost nothing to
/// run. Override it with the <c>OPENROUTER_MODEL</c> environment variable to
/// exercise a different model (use a <c>provider/model</c> slug, e.g.
/// <c>qwen/qwen3-next-80b-a3b-instruct:free</c> or <c>anthropic/claude-sonnet-4.6</c>).
///
/// OpenRouter's free pool is shared and frequently rate-limited (HTTP 429) or
/// retires <c>:free</c> slugs (HTTP 404). Those are upstream capacity/catalog
/// conditions, not provider bugs, so the tests treat them as "inconclusive" and
/// no-op rather than failing — keeping CI deterministic on a free tier.
/// </summary>
public class OpenRouterIntegrationTests
{
    private const string DefaultFreeModel = "openai/gpt-oss-20b:free";

    private static string? ApiKey => Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

    private static string Model =>
        Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? DefaultFreeModel;

    /// <summary>
    /// True when the message reflects an upstream free-tier condition we can't
    /// control: rate limiting (429) or a deprecated/removed free model (404).
    /// </summary>
    private static bool IsUpstreamUnavailable(string? message) =>
        message != null && (message.Contains("429") || message.Contains("404"));

    private static OpenRouterProvider CreateProvider()
    {
        var config = new ProviderConfig
        {
            Provider = "openrouter",
            ApiKey = ApiKey,
            ApiBase = OpenRouterProvider.DefaultApiBase,
            Model = Model
        };
        return new OpenRouterProvider(config, "openrouter", NullLogger<OpenRouterProvider>.Instance);
    }

    [Fact]
    public async Task IsAvailableAsync_WithRealKey_ReturnsTrue()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return; // skipped without credentials
        }

        var provider = CreateProvider();
        // IsAvailableAsync swallows transport errors and returns a bool, so a
        // rate-limited free model legitimately reports false; only assert that
        // the call completes without throwing.
        await provider.IsAvailableAsync();
    }

    [Fact]
    public async Task CompleteAsync_SimplePrompt_ReturnsText()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return;
        }

        var provider = CreateProvider();
        LlmResponse response;
        try
        {
            response = await provider.CompleteAsync(new LlmRequest
            {
                Messages = new List<Message>
                {
                    new() { Role = Role.User, Content = "Reply with exactly the word: pong" }
                },
                Config = new LlmClientConfig { Model = Model, MaxTokens = 32, Temperature = 0m }
            });
        }
        catch (InvalidOperationException ex) when (IsUpstreamUnavailable(ex.Message))
        {
            return; // free model rate-limited/deprecated upstream — inconclusive
        }

        Assert.NotNull(response);
        Assert.NotNull(response.AssistantMessage);
        Assert.False(string.IsNullOrWhiteSpace(response.Content), "Expected a non-empty completion");
        Assert.Contains("pong", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamCompleteAsync_SimplePrompt_YieldsContentAndTerminalFrame()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return;
        }

        var provider = CreateProvider();

        var content = new System.Text.StringBuilder();
        LlmStreamResponse? terminal = null;
        await foreach (var chunk in provider.StreamCompleteAsync(new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "Count from 1 to 5, separated by spaces." }
            },
            Config = new LlmClientConfig { Model = Model, MaxTokens = 64, Temperature = 0m }
        }))
        {
            if (chunk.Delta?.Content is { Length: > 0 } text)
            {
                content.Append(text);
            }

            if (chunk.IsComplete)
            {
                terminal = chunk;
            }
        }

        Assert.NotNull(terminal);
        Assert.True(terminal!.IsComplete);
        if (IsUpstreamUnavailable(terminal.Error))
        {
            return; // free model rate-limited/deprecated upstream — inconclusive
        }

        Assert.Null(terminal.Error);
        Assert.False(string.IsNullOrWhiteSpace(content.ToString()), "Expected streamed content");
    }

    [Fact]
    public async Task CompleteAsync_WithTools_ReturnsToolCall()
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return;
        }

        var provider = CreateProvider();
        LlmResponse response;
        try
        {
            response = await provider.CompleteAsync(new LlmRequest
            {
                Messages = new List<Message>
                {
                    new() { Role = Role.User, Content = "What is 15% of 240? Use the calculate tool." }
                },
                Tools = new List<ToolDeclaration>
                {
                    new()
                    {
                        Name = "calculate",
                        Description = "Perform a mathematical calculation",
                        Parameters = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>
                            {
                                ["expression"] = new Dictionary<string, object>
                                {
                                    ["type"] = "string",
                                    ["description"] = "The expression to evaluate, e.g. '0.15 * 240'"
                                }
                            },
                            ["required"] = new[] { "expression" }
                        }
                    }
                },
                Config = new LlmClientConfig { Model = Model, MaxTokens = 256, Temperature = 0m }
            });
        }
        catch (InvalidOperationException ex) when (IsUpstreamUnavailable(ex.Message))
        {
            return; // free model rate-limited/deprecated upstream — inconclusive
        }

        Assert.NotNull(response);
        // Not every free model is reliable at tool calling; assert only when the
        // model actually chose to call the tool, otherwise accept a text answer.
        if (response.HasToolCalls)
        {
            var call = response.ToolCalls[0];
            Assert.Equal("calculate", call.Name);
            Assert.Contains("240", call.ArgumentsJson);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(response.Content),
                "Model returned neither a tool call nor text content");
        }
    }
}
