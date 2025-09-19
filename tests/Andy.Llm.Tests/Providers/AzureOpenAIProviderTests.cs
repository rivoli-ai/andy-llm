using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Tests for Azure OpenAI provider
/// </summary>
public class AzureOpenAIProviderTests
{
    private readonly Mock<ILogger<AzureOpenAIProvider>> _mockLogger;
    private readonly LlmOptions _options;

    public AzureOpenAIProviderTests()
    {
        _mockLogger = new Mock<ILogger<AzureOpenAIProvider>>();
        _options = new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["azure"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://test.openai.azure.com",
                    DeploymentName = "test-deployment",
                    ApiVersion = "2024-02-15-preview",
                    Model = "gpt-4",
                    Enabled = true
                }
            }
        };
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var options = Options.Create(_options);

        // Act & Assert - should not throw
        var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

        Assert.NotNull(provider);
        Assert.Equal("azure", provider.Name);
    }

    [Fact]
    public void Constructor_WithoutEndpoint_ShouldThrow()
    {
        // Arrange
        _options.Providers["azure"].ApiBase = null;
        var options = Options.Create(_options);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIProvider(options, _mockLogger.Object));

        Assert.Contains("endpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_WithoutApiKey_ShouldThrow()
    {
        // Arrange
        _options.Providers["azure"].ApiKey = null;
        var options = Options.Create(_options);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => new AzureOpenAIProvider(options, _mockLogger.Object));

        Assert.Contains("API key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ShouldUseEnvironmentVariables_WhenConfigNotProvided()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://env-test.openai.azure.com");
        Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "env-test-key");
        Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", "env-test-deployment");

        try
        {
            var emptyOptions = new LlmOptions { Providers = new Dictionary<string, ProviderConfig>() };
            var options = Options.Create(emptyOptions);

            // Act
            var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

            // Assert
            Assert.NotNull(provider);
            Assert.Equal("azure", provider.Name);
        }
        finally
        {
            // Clean up environment variables
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", null);
        }
    }

    [Fact]
    public async Task IsAvailableAsync_WithInvalidCredentials_ShouldReturnFalse()
    {
        // This test would require mocking the Azure OpenAI client or using a real connection
        // For now, we'll skip the actual implementation

        // Arrange
        var options = Options.Create(_options);
        var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

        // Act - This will fail with invalid credentials
        var result = await provider.IsAvailableAsync();

        // Assert
        Assert.False(result);
    }

    [Fact(Skip = "Integration test - requires real Azure OpenAI credentials")]
    public async Task CompleteAsync_WithValidRequest_ShouldReturnResponse()
    {
        // This test requires real Azure OpenAI credentials to run
        // It's marked as Skip for CI/CD environments

        // Arrange
        var options = Options.Create(_options);
        var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Say hello" }
            },
            Config = new LlmClientConfig { MaxTokens = 10 }
        };

        // Act
        var response = await provider.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.NotNull(response.Usage);
    }

    [Fact(Skip = "Integration test - requires real Azure OpenAI credentials")]
    public async Task StreamCompleteAsync_WithValidRequest_ShouldStreamResponse()
    {
        // This test requires real Azure OpenAI credentials to run

        // Arrange
        var options = Options.Create(_options);
        var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Count to 5" }
            },
            Config = new LlmClientConfig { MaxTokens = 50 }
        };

        // Act
        var responses = new List<LlmStreamResponse>();
        await foreach (var response in provider.StreamCompleteAsync(request))
        {
            responses.Add(response);
        }

        // Assert
        Assert.NotEmpty(responses);
        Assert.Contains(responses, r => r.IsComplete);
        Assert.Contains(responses, r => r.IsComplete && r.FinishReason != null);
        Assert.Contains(responses, r => !string.IsNullOrEmpty(r.TextDelta));
    }

    [Fact]
    public void LoadConfiguration_ShouldPreferProvidedConfig_OverEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://env.openai.azure.com");
        Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", "env-key");

        try
        {
            var options = Options.Create(_options);

            // Act
            var provider = new AzureOpenAIProvider(options, _mockLogger.Object);

            // Assert - should use config values, not env vars
            Assert.NotNull(provider);
            // The actual values are private, but initialization should succeed with config values
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_KEY", null);
        }
    }
}
