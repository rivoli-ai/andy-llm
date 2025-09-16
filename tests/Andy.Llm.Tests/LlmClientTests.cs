using Xunit;
using Andy.Llm;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
using Andy.Llm.Services;
using Andy.Context.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenAI;

namespace Andy.Llm.Tests;

/// <summary>
/// Unit tests for the <see cref="LlmClient"/> class.
/// </summary>
public class LlmClientTests
{
    /// <summary>
    /// Tests that the LlmClient constructor with API key creates a valid instance.
    /// </summary>
    [Fact]
    public void Constructor_WithApiKey_ShouldCreateInstance()
    {
        // Arrange & Act
        var client = new LlmClient("test-api-key");

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(client.GetChatClient()); // Legacy compatibility check
    }

    /// <summary>
    /// Tests that the LlmClient constructor throws an ArgumentNullException when provided a null API key.
    /// </summary>
    [Fact]
    public void Constructor_WithNullApiKey_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LlmClient((string)null!));
    }

    /// <summary>
    /// Tests that the LlmClient constructor with OpenAIClient creates a valid instance.
    /// </summary>
    [Fact]
    public void Constructor_WithOpenAIClient_ShouldCreateInstance()
    {
        // Arrange
        var openAiClient = new OpenAI.OpenAIClient("test-key");

        // Act
        var client = new LlmClient(openAiClient);

        // Assert
        Assert.NotNull(client);
        Assert.NotNull(client.GetChatClient()); // Legacy compatibility check
    }

    /// <summary>
    /// Tests that the LlmClient constructor with provider factory creates a valid instance.
    /// </summary>
    [Fact]
    public void Constructor_WithProviderFactory_ShouldCreateInstance()
    {
        // Arrange
        var mockFactory = new Mock<ILlmProviderFactory>();
        var mockLogger = new Mock<ILogger<LlmClient>>();

        // Act
        var client = new LlmClient(mockFactory.Object, mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// Tests that the LlmClient constructor with provider creates a valid instance.
    /// </summary>
    [Fact]
    public void Constructor_WithProvider_ShouldCreateInstance()
    {
        // Arrange
        var mockProvider = new Mock<ILlmProvider>();
        var mockLogger = new Mock<ILogger<LlmClient>>();

        // Act
        var client = new LlmClient(mockProvider.Object, mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// Tests that CompleteAsync works with a provider.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_WithProvider_ShouldReturnResponse()
    {
        // Arrange
        var mockProvider = new Mock<ILlmProvider>();
        var mockLogger = new Mock<ILogger<LlmClient>>();
        var expectedResponse = new LlmResponse { Content = "Test response" };

        mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), default))
            .ReturnsAsync(expectedResponse);

        var client = new LlmClient(mockProvider.Object, mockLogger.Object);
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Hello" }
            }
        };

        // Act
        var response = await client.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Test response", response.Content);
        mockProvider.Verify(p => p.CompleteAsync(It.IsAny<LlmRequest>(), default), Times.Once);
    }

    /// <summary>
    /// Tests GetResponseAsync with legacy API key constructor.
    /// </summary>
    [Fact]
    public async Task GetResponseAsync_WithApiKey_ShouldWork()
    {
        // Act & Assert
        // This will throw because the API key is invalid
        // but verifies the method exists for compatibility
        await Assert.ThrowsAsync<System.ClientModel.ClientResultException>(
            async () =>
            {
                var client = new LlmClient("test-api-key");
                await client.GetResponseAsync("Hello", "gpt-4");
            }
        );
    }

    /// <summary>
    /// Tests that the LlmClient constructor throws an ArgumentNullException when provided a null OpenAIClient.
    /// </summary>
    [Fact]
    public void Constructor_WithNullOpenAIClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LlmClient((OpenAI.OpenAIClient)null!));
    }

    /// <summary>
    /// Tests that the LlmClient constructor throws an ArgumentNullException when provided a null provider factory.
    /// </summary>
    [Fact]
    public void Constructor_WithNullProviderFactory_ShouldThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LlmClient>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LlmClient((ILlmProviderFactory)null!, mockLogger.Object));
    }

    /// <summary>
    /// Tests that the LlmClient constructor throws an ArgumentNullException when provided a null provider.
    /// </summary>
    [Fact]
    public void Constructor_WithNullProvider_ShouldThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LlmClient>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LlmClient((ILlmProvider)null!, mockLogger.Object));
    }
}
