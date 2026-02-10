using Andy.Llm.Configuration;
using Xunit;

namespace Andy.Llm.Tests.Configuration;

/// <summary>
/// Tests for ProviderConfig properties including the new Provider and ApiType fields.
/// </summary>
public class ProviderConfigTests
{
    [Fact]
    public void ProviderConfig_DefaultValues_AreCorrect()
    {
        var config = new ProviderConfig();

        Assert.Null(config.Provider);
        Assert.Null(config.ApiType);
        Assert.Null(config.ApiKey);
        Assert.Null(config.ApiBase);
        Assert.Null(config.Model);
        Assert.Null(config.Organization);
        Assert.Null(config.ApiVersion);
        Assert.Null(config.DeploymentName);
        Assert.Null(config.AdditionalSettings);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void ProviderConfig_Provider_CanBeSet()
    {
        var config = new ProviderConfig { Provider = "openai" };
        Assert.Equal("openai", config.Provider);
    }

    [Fact]
    public void ProviderConfig_ApiType_CanBeSetToChatCompletions()
    {
        var config = new ProviderConfig { ApiType = "chat-completions" };
        Assert.Equal("chat-completions", config.ApiType);
    }

    [Fact]
    public void ProviderConfig_ApiType_CanBeSetToResponses()
    {
        var config = new ProviderConfig { ApiType = "responses" };
        Assert.Equal("responses", config.ApiType);
    }

    [Fact]
    public void ProviderConfig_CompoundAlias_HasAllFields()
    {
        // Simulates config for "openai/codex-mini" entry
        var config = new ProviderConfig
        {
            Provider = "openai",
            ApiType = "responses",
            ApiKey = "sk-test",
            ApiBase = "https://api.openai.com/v1",
            Model = "codex-mini-latest",
            Enabled = true
        };

        Assert.Equal("openai", config.Provider);
        Assert.Equal("responses", config.ApiType);
        Assert.Equal("codex-mini-latest", config.Model);
    }

    [Fact]
    public void LlmOptions_DefaultValues_AreCorrect()
    {
        var options = new LlmOptions();

        Assert.Equal("openai", options.DefaultProvider);
        Assert.NotNull(options.Providers);
        Assert.Empty(options.Providers);
        Assert.Null(options.DefaultModel);
        Assert.Equal(0.7, options.DefaultTemperature);
        Assert.Equal(4096, options.DefaultMaxTokens);
    }

    [Fact]
    public void LlmOptions_Providers_SupportsCompoundKeys()
    {
        var options = new LlmOptions
        {
            DefaultProvider = "openai/codex-mini",
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig { Provider = "openai", Model = "gpt-4o" },
                ["openai/codex-mini"] = new ProviderConfig { Provider = "openai", Model = "codex-mini-latest", ApiType = "responses" },
                ["openai/codex-5.1"] = new ProviderConfig { Provider = "openai", Model = "gpt-5.1-codex", ApiType = "responses" }
            }
        };

        Assert.Equal(3, options.Providers.Count);
        Assert.True(options.Providers.ContainsKey("openai/codex-mini"));
        Assert.Equal("codex-mini-latest", options.Providers["openai/codex-mini"].Model);
        Assert.Equal("responses", options.Providers["openai/codex-mini"].ApiType);
    }
}
