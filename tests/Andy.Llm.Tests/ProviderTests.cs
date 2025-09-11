using Xunit;
using Andy.Llm;
using Andy.Llm.Abstractions;
using Andy.Llm.Models;
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
                    Model = "gpt-4"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;

        // Act & Assert - should not throw
        var provider = new OpenAIProvider(options, logger);
        Assert.NotNull(provider);
        Assert.Equal("OpenAI", provider.Name);
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
        Assert.Equal("Cerebras", provider.Name);
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
                ["openai"] = new ProviderConfig { ApiKey = "test-key" },
                ["cerebras"] = new ProviderConfig { ApiKey = "test-key" }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act
        var openaiProvider = factory.CreateProvider("openai");
        var cerebrasProvider = factory.CreateProvider("cerebras");

        // Assert
        Assert.NotNull(openaiProvider);
        Assert.Equal("OpenAI", openaiProvider.Name);
        Assert.NotNull(cerebrasProvider);
        Assert.Equal("Cerebras", cerebrasProvider.Name);
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
    /// Tests message conversion for different roles
    /// </summary>
    [Fact]
    public void MessageConversion_ShouldHandleAllRoles()
    {
        // Arrange
        var context = new ConversationContext();

        // Act
        context.SystemInstruction = "You are a helpful assistant";
        context.AddUserMessage("Hello");
        context.AddAssistantMessage("Hi there!");

        var functionCalls = new List<FunctionCall>
        {
            new FunctionCall
            {
                Id = "call_123",
                Name = "get_weather",
                Arguments = new Dictionary<string, object?> { ["location"] = "NYC" }
            }
        };
        context.AddAssistantMessageWithToolCalls(null, functionCalls);
        context.AddToolResponse("get_weather", "call_123", new { temp = 72 });

        // Assert
        Assert.Equal(5, context.Messages.Count);
        Assert.Equal(MessageRole.System, context.Messages[0].Role);
        Assert.Equal(MessageRole.User, context.Messages[1].Role);
        Assert.Equal(MessageRole.Assistant, context.Messages[2].Role);
        Assert.Equal(MessageRole.Assistant, context.Messages[3].Role);
        Assert.Equal(MessageRole.Tool, context.Messages[4].Role);

        // Check tool call part
        var toolCallMessage = context.Messages[3];
        Assert.Single(toolCallMessage.Parts);
        var toolCallPart = toolCallMessage.Parts[0] as ToolCallPart;
        Assert.NotNull(toolCallPart);
        Assert.Equal("get_weather", toolCallPart.ToolName);
        Assert.Equal("call_123", toolCallPart.CallId);
        Assert.NotNull(toolCallPart.Arguments);
    }
}
