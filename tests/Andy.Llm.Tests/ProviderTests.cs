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

    /// <summary>
    /// Tests that temperature in Config is properly nullable and defaults to null
    /// </summary>
    [Fact]
    public void LlmRequest_Temperature_ShouldDefaultToNull()
    {
        // Arrange & Act - Create request without setting temperature
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "test" }
            },
            Config = new LlmClientConfig
            {
                MaxTokens = 100
            }
        };

        // Assert - Temperature should be null
        Assert.Null(request.Config.Temperature);
        Assert.Null(request.Temperature);
    }

    /// <summary>
    /// Tests that temperature can be explicitly set and is properly propagated
    /// </summary>
    [Fact]
    public void LlmRequest_Temperature_ShouldPropagateWhenSet()
    {
        // Arrange & Act - Create request with explicit temperature
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "test" }
            },
            Config = new LlmClientConfig
            {
                Temperature = 0.5m,
                MaxTokens = 100
            }
        };

        // Assert - Temperature should match configured value
        Assert.Equal(0.5m, request.Config.Temperature);
        Assert.Equal(0.5m, request.Temperature);
    }

    /// <summary>
    /// Tests that null temperature in Config results in null request temperature
    /// </summary>
    [Fact]
    public void LlmRequest_Temperature_ShouldBeNullWhenConfigIsNull()
    {
        // Arrange & Act - Create request without Config
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "test" }
            }
        };

        // Assert - Temperature should be null
        Assert.Null(request.Temperature);
    }

}
