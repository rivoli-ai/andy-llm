using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Andy.Llm;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Andy.Llm.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for individual provider implementations
/// </summary>
public class ProviderTests
{
    /// <summary>
    /// Tests that OpenAI provider initializes correctly with API key
    /// </summary>
    [Fact]
    public void OpenAIProvider_ShouldInitialize_WithApiKey()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;

        // Act & Assert - should not throw
        var provider = new OpenAIProvider(options, logger);
        Assert.NotNull(provider);
        Assert.Equal("openai", provider.Name);  // Name matches configuration key
    }

    /// <summary>
    /// Tests that Cerebras provider initializes correctly with API key
    /// </summary>
    [Fact]
    public void CerebrasProvider_ShouldInitialize_WithApiKey()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    Model = "llama3.1-70b",
                    ApiBase = "https://api.cerebras.ai/v1"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act & Assert - should not throw
        var provider = new CerebrasProvider(options, logger);
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);  // Name matches configuration key
    }

    /// <summary>
    /// Tests that provider factory creates correct provider
    /// </summary>
    [Fact]
    public void ProviderFactory_ShouldCreateCorrectProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options =>
        {
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4"
                },
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama3.1-70b"
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act
        var openaiProvider = factory.CreateProvider("openai");
        var cerebrasProvider = factory.CreateProvider("cerebras");

        // Assert
        Assert.NotNull(openaiProvider);
        Assert.Equal("openai", openaiProvider.Name);  // Name matches configuration key
        Assert.NotNull(cerebrasProvider);
        Assert.Equal("cerebras", cerebrasProvider.Name);  // Name matches configuration key
    }

    /// <summary>
    /// Tests that provider factory throws for unsupported provider
    /// </summary>
    [Fact]
    public void ProviderFactory_ShouldThrow_ForUnsupportedProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(options => { });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => factory.CreateProvider("unsupported"));
    }

}
