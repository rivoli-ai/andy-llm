using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Tests for LlmProviderFactory, including compound alias routing,
/// Provider field resolution, and caching behavior.
/// </summary>
public class ProviderFactoryTests
{
    private const string TestApiKey = "test-key";
    private const string TestApiBase = "https://api.openai.com/v1";
    private const string CerebrasApiBase = "https://api.cerebras.ai/v1";

    [Fact]
    public void CreateProvider_BaseKey_ReturnsCorrectProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = TestApiKey, ApiBase = TestApiBase, Model = "gpt-4o", Provider = "openai"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("openai");
        Assert.NotNull(provider);
        Assert.Equal("openai", provider.Name);
    }

    [Fact]
    public void CreateProvider_CompoundKey_WithProviderField_ReturnsCorrectType()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = TestApiKey, ApiBase = TestApiBase, Model = "gpt-4o", Provider = "openai"
            };
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "codex-mini-latest",
                ApiType = "responses"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("openai/codex-mini");
        Assert.NotNull(provider);

        // Should be a different instance from the base "openai" provider
        var baseProvider = factory.CreateProvider("openai");
        Assert.NotSame(provider, baseProvider);
    }

    [Fact]
    public void CreateProvider_CompoundKey_UsesCorrectModel()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = TestApiKey, ApiBase = TestApiBase, Model = "gpt-4o"
            };
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "codex-mini-latest"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("openai/codex-mini") as OpenAIProvider;
        Assert.NotNull(provider);
        Assert.Equal("codex-mini-latest", provider.DefaultModel);
    }

    [Fact]
    public void CreateProvider_CompoundKey_AutoDetectsApiType()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "codex-mini-latest"
                // No explicit ApiType — should auto-detect "responses"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("openai/codex-mini") as OpenAIProvider;
        Assert.NotNull(provider);
        Assert.Equal("responses", provider.ApiType);
    }

    [Fact]
    public void CreateProvider_CompoundKey_WithoutProviderField_InfersFromName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            // No Provider field, but name starts with "openai/"
            options.Providers["openai/gpt4-turbo"] = new ProviderConfig
            {
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "gpt-4-turbo"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("openai/gpt4-turbo");
        Assert.NotNull(provider);
    }

    [Fact]
    public void CreateProvider_CachesInstances()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai/codex"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "gpt-5-codex"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider1 = factory.CreateProvider("openai/codex");
        var provider2 = factory.CreateProvider("openai/codex");
        Assert.Same(provider1, provider2);
    }

    [Fact]
    public void CreateProvider_UnknownProvider_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options => { });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        Assert.Throws<NotSupportedException>(() => factory.CreateProvider("unsupported"));
    }

    [Fact]
    public void CreateProvider_CompoundKey_NoConfig_ThrowsDescriptiveError()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = TestApiKey, ApiBase = TestApiBase, Model = "gpt-4o"
            };
            // No "openai/missing" config entry
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        // Factory throws NotSupportedException when config key is not found
        var ex = Assert.Throws<NotSupportedException>(() => factory.CreateProvider("openai/missing"));
        Assert.Contains("openai/missing", ex.Message);
    }

    [Fact]
    public void CreateProvider_DefaultProvider_UsesConfiguredDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai/codex-mini";
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = TestApiKey, ApiBase = TestApiBase, Model = "gpt-4o"
            };
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "codex-mini-latest"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        // CreateProvider(null) should use the default
        var provider = factory.CreateProvider() as OpenAIProvider;
        Assert.NotNull(provider);
        Assert.Equal("codex-mini-latest", provider.DefaultModel);
    }

    [Fact]
    public void CreateProvider_CerebrasCompoundKey_Works()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["cerebras"] = new ProviderConfig
            {
                ApiKey = TestApiKey,
                ApiBase = CerebrasApiBase,
                Model = "llama3.1-8b"
            };
            options.Providers["cerebras/llama-70b"] = new ProviderConfig
            {
                Provider = "cerebras",
                ApiKey = TestApiKey,
                ApiBase = CerebrasApiBase,
                Model = "llama-3.3-70b"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var provider = factory.CreateProvider("cerebras/llama-70b");
        Assert.NotNull(provider);
    }

    [Fact]
    public void CreateProvider_MultipleCompoundKeys_AreSeparateInstances()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers["openai/codex-mini"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "codex-mini-latest"
            };
            options.Providers["openai/codex-5.1"] = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = TestApiKey,
                ApiBase = TestApiBase,
                Model = "gpt-5.1-codex"
            };
        });

        var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<ILlmProviderFactory>();

        var mini = factory.CreateProvider("openai/codex-mini") as OpenAIProvider;
        var v51 = factory.CreateProvider("openai/codex-5.1") as OpenAIProvider;

        Assert.NotNull(mini);
        Assert.NotNull(v51);
        Assert.NotSame(mini, v51);
        Assert.Equal("codex-mini-latest", mini.DefaultModel);
        Assert.Equal("gpt-5.1-codex", v51.DefaultModel);
    }
}
