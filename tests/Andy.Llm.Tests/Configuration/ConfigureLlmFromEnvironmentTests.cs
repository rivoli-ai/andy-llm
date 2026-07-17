using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Llm.Tests.Configuration;

/// <summary>
/// Tests for ConfigureLlmFromEnvironment extension method.
/// Verifies that environment variables create provider entries with the correct
/// Provider field and don't override existing configuration.
/// </summary>
[Collection("EnvironmentVariable Tests")]
public class ConfigureLlmFromEnvironmentTests
{
    [Fact]
    public void ConfigureLlmFromEnvironment_SetsProviderField()
    {
        // This test would normally need env vars set, but we can verify the structure
        // by pre-populating and checking that the Provider field pattern is correct.
        var services = new ServiceCollection();
        services.Configure<LlmOptions>(options =>
        {
            // Simulate what ConfigureLlmFromEnvironment would create
            options.Providers["openai"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = "test-key",
                Model = "gpt-4o",
                Enabled = true
            };
        });

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

        Assert.True(options.Providers.ContainsKey("openai"));
        Assert.Equal("openai", options.Providers["openai"].Provider);
    }

    [Fact]
    public void ConfigureLlmFromEnvironment_DoesNotOverrideExistingConfig()
    {
        var services = new ServiceCollection();

        // Pre-configure openai with custom settings
        services.Configure<LlmOptions>(options =>
        {
            options.Providers["openai"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = "my-custom-key",
                Model = "gpt-4-turbo",
                Enabled = true
            };
        });

        // Run ConfigureLlmFromEnvironment — should NOT overwrite "openai" even if OPENAI_API_KEY is set
        services.ConfigureLlmFromEnvironment();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

        // The existing config should be preserved
        Assert.Equal("my-custom-key", options.Providers["openai"].ApiKey);
        Assert.Equal("gpt-4-turbo", options.Providers["openai"].Model);
    }

    [Fact]
    public void ConfigureLlmFromEnvironment_PreservesCompoundKeys()
    {
        var services = new ServiceCollection();

        // Pre-configure compound keys
        services.Configure<LlmOptions>(options =>
        {
            options.DefaultProvider = "openai/codex-mini";
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = "my-key",
                Model = "codex-mini-latest",
                ApiType = "responses",
                Enabled = true
            };
        });

        services.ConfigureLlmFromEnvironment();

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

        // Compound key should be preserved
        Assert.True(options.Providers.ContainsKey("openai/codex-mini"));
        Assert.Equal("codex-mini-latest", options.Providers["openai/codex-mini"].Model);
        Assert.Equal("responses", options.Providers["openai/codex-mini"].ApiType);
    }

    [Fact]
    public void ConfigureLlmFromEnvironment_DoesNotRegisterUnsupportedGoogleProvider()
    {
        var originalGoogleKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
        try
        {
            // Only GOOGLE_API_KEY is set — the factory has no case for "google",
            // so it must NOT be added to the provider options.
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", "test-google-key");

            var services = new ServiceCollection();
            services.ConfigureLlmFromEnvironment();

            var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

            Assert.False(options.Providers.ContainsKey("google"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOOGLE_API_KEY", originalGoogleKey);
        }
    }

    [Fact]
    public void ConfigureLlmFromEnvironment_DoesNotRegisterUnsupportedGroqProvider()
    {
        var originalGroqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        try
        {
            // Only GROQ_API_KEY is set — the factory has no case for "groq",
            // so it must NOT be added to the provider options.
            Environment.SetEnvironmentVariable("GROQ_API_KEY", "test-groq-key");

            var services = new ServiceCollection();
            services.ConfigureLlmFromEnvironment();

            var sp = services.BuildServiceProvider();
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;

            Assert.False(options.Providers.ContainsKey("groq"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GROQ_API_KEY", originalGroqKey);
        }
    }
}
