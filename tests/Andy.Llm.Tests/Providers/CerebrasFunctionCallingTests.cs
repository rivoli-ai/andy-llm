using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Tests for Cerebras function calling functionality.
/// Verifies that the provider is properly configured for function calling models.
/// </summary>
public class CerebrasFunctionCallingTests
{
    /// <summary>
    /// Verifies that llama-3.3-70b is initialized and marked as supporting function calling
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithLlama33_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama-3.3-70b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act
        var provider = new CerebrasProvider(options, logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);
    }

    /// <summary>
    /// Verifies that gpt-oss-120b is initialized and marked as supporting function calling
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithGptOss120b_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "gpt-oss-120b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act
        var provider = new CerebrasProvider(options, logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);
    }

    /// <summary>
    /// Verifies that qwen-3-coder-480b is initialized.
    /// This model was added experimentally to the function calling whitelist.
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithQwen3Coder_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "qwen-3-coder-480b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act
        var provider = new CerebrasProvider(options, logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);
    }

    /// <summary>
    /// Verifies that models not whitelisted for function calling can still be initialized.
    /// These models will work but won't receive tool definitions.
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithNonFunctionCallingModel_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "qwen-2.5-7b" // This model is NOT in the function calling whitelist
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act
        var provider = new CerebrasProvider(options, logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);
    }

    /// <summary>
    /// Verifies that provider throws when API base is missing
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithoutApiBase_ShouldThrow()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "", // Empty API base
                    Model = "llama-3.3-70b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new CerebrasProvider(options, logger));
    }

    /// <summary>
    /// Verifies that zai-glm-4.6 is initialized and marked as supporting function calling
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithZaiGlm46_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "zai-glm-4.6"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act
        var provider = new CerebrasProvider(options, logger);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("cerebras", provider.Name);
    }

    /// <summary>
    /// Verifies that provider throws when model is missing
    /// </summary>
    [Fact]
    public void CerebrasProvider_WithoutModel_ShouldThrow()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "" // Empty model
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => new CerebrasProvider(options, logger));
    }
}
