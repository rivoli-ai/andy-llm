using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Tests for API type detection and strategy selection in OpenAIProvider.
/// Verifies that models are correctly routed to Chat Completions or Responses API.
/// </summary>
public class OpenAIApiTypeDetectionTests
{
    #region DetectApiType Tests

    [Theory]
    [InlineData("chat-completions", "gpt-4o", "chat-completions")]
    [InlineData("responses", "gpt-4o", "responses")]
    [InlineData("RESPONSES", "gpt-4o", "responses")]
    [InlineData("Chat-Completions", "codex-mini-latest", "chat-completions")]
    public void DetectApiType_ExplicitConfig_TakesPrecedence(string configured, string model, string expected)
    {
        var result = OpenAIProvider.DetectApiType(configured, model);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("codex-mini-latest", "responses")]
    [InlineData("gpt-5-codex", "responses")]
    [InlineData("gpt-5.1-codex-mini", "responses")]
    [InlineData("gpt-5.1-codex-max", "responses")]
    [InlineData("gpt-5.1-codex", "responses")]
    [InlineData("gpt-5.2-codex", "responses")]
    public void DetectApiType_CodexModels_AutoDetectResponses(string model, string expected)
    {
        var result = OpenAIProvider.DetectApiType(null, model);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("gpt-4o", "chat-completions")]
    [InlineData("gpt-4-turbo", "chat-completions")]
    [InlineData("gpt-3.5-turbo", "chat-completions")]
    [InlineData("gpt-4", "chat-completions")]
    public void DetectApiType_StandardModels_AutoDetectChatCompletions(string model, string expected)
    {
        var result = OpenAIProvider.DetectApiType(null, model);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DetectApiType_NullModel_DefaultsToChatCompletions()
    {
        var result = OpenAIProvider.DetectApiType(null, null);
        Assert.Equal("chat-completions", result);
    }

    [Fact]
    public void DetectApiType_EmptyConfig_AutoDetects()
    {
        var result = OpenAIProvider.DetectApiType("", "codex-mini-latest");
        Assert.Equal("responses", result);
    }

    #endregion

    #region RequiresResponsesApi Tests

    [Theory]
    [InlineData("codex-mini-latest", true)]
    [InlineData("gpt-5-codex", true)]
    [InlineData("gpt-5.1-codex-mini", true)]
    [InlineData("gpt-5.1-codex-max", true)]
    [InlineData("gpt-5.1-codex", true)]
    [InlineData("gpt-5.2-codex", true)]
    [InlineData("CODEX-MINI-LATEST", true)]
    public void RequiresResponsesApi_CodexModels_ReturnsTrue(string model, bool expected)
    {
        Assert.Equal(expected, OpenAIProvider.RequiresResponsesApi(model));
    }

    [Theory]
    [InlineData("gpt-4o", false)]
    [InlineData("gpt-4-turbo", false)]
    [InlineData("gpt-3.5-turbo", false)]
    [InlineData("dall-e-3", false)]
    [InlineData("text-embedding-3-small", false)]
    public void RequiresResponsesApi_NonCodexModels_ReturnsFalse(string model, bool expected)
    {
        Assert.Equal(expected, OpenAIProvider.RequiresResponsesApi(model));
    }

    #endregion

    #region Provider Initialization Tests

    [Fact]
    public void OpenAIProvider_WithChatCompletionsModel_UsesChatCompletionsStrategy()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4o"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;
        var provider = new OpenAIProvider(options, logger);

        Assert.Equal("chat-completions", provider.ApiType);
        Assert.Equal("gpt-4o", provider.DefaultModel);
    }

    [Fact]
    public void OpenAIProvider_WithCodexModel_UsesResponsesStrategy()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "codex-mini-latest"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;
        var provider = new OpenAIProvider(options, logger);

        Assert.Equal("responses", provider.ApiType);
        Assert.Equal("codex-mini-latest", provider.DefaultModel);
    }

    [Fact]
    public void OpenAIProvider_WithExplicitApiType_UsesConfiguredStrategy()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4o",
                    ApiType = "responses" // Force Responses API even for gpt-4o
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;
        var provider = new OpenAIProvider(options, logger);

        Assert.Equal("responses", provider.ApiType);
    }

    [Fact]
    public void OpenAIProvider_WithExplicitChatCompletions_OverridesAutoDetect()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "codex-mini-latest",
                    ApiType = "chat-completions" // Force Chat Completions even for codex
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;
        var provider = new OpenAIProvider(options, logger);

        Assert.Equal("chat-completions", provider.ApiType);
    }

    #endregion
}
