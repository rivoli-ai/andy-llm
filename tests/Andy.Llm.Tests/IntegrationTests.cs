using Xunit;
using Andy.Llm;
using Andy.Llm.Abstractions;
using Andy.Llm.Models;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Andy.Context.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Tests;

/// <summary>
/// Integration tests for LLM providers.
/// These tests require environment variables to be set for the respective providers.
/// </summary>
public class IntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public IntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Tests OpenAI provider when OPENAI_API_KEY is set.
    /// </summary>
    [SkippableFact]
    public async Task OpenAI_CompleteAsync_ShouldWork()
    {
        if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") == null)
        {
            return; // Skip test silently if API key not set
        }

        var provider = _fixture.GetProvider("openai");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Say 'Hello, World!' and nothing else." }
            },
            Model = "gpt-4o-mini",
            MaxTokens = 50,
            Temperature = 0
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests Cerebras provider when CEREBRAS_API_KEY is set.
    /// </summary>
    [SkippableFact]
    public async Task Cerebras_CompleteAsync_ShouldWork()
    {
        if (Environment.GetEnvironmentVariable("CEREBRAS_API_KEY") == null)
        {
            return; // Skip test silently if API key not set
        }

        var provider = _fixture.GetProvider("cerebras");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Say 'Hello, World!' and nothing else." }
            },
            Model = "llama3.1-8b",
            MaxTokens = 50,
            Temperature = 0
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests streaming with OpenAI provider.
    /// </summary>
    [SkippableFact]
    public async Task OpenAI_StreamCompleteAsync_ShouldWork()
    {
        if (Environment.GetEnvironmentVariable("OPENAI_API_KEY") == null)
        {
            return; // Skip test silently if API key not set
        }

        var provider = _fixture.GetProvider("openai");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Count from 1 to 5." }
            },
            Model = "gpt-4o-mini",
            MaxTokens = 100,
            Temperature = 0,
            Stream = true
        };

        var chunks = new List<string>();
        await foreach (var chunk in provider.StreamCompleteAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                chunks.Add(chunk.TextDelta);
            }
        }

        Assert.NotEmpty(chunks);
        var fullText = string.Join("", chunks);
        Assert.Contains("1", fullText);
        Assert.Contains("5", fullText);
    }

    /// <summary>
    /// Tests conversation context management.
    /// </summary>
    [Fact]
    public void ConversationContext_ShouldManageMessages()
    {
        var context = new ConversationContext();

        context.SystemInstruction = "You are a helpful assistant.";
        context.AddUserMessage("Hello!");
        context.AddAssistantMessage("Hi there! How can I help you?");
        context.AddUserMessage("What's 2+2?");
        context.AddAssistantMessage("2+2 equals 4.");

        Assert.Equal(5, context.Messages.Count); // System + 4 messages
        Assert.Equal(Role.System, context.Messages[0].Role);
        Assert.Equal(Role.User, context.Messages[1].Role);
        Assert.Equal(Role.Assistant, context.Messages[2].Role);

        var request = context.CreateRequest("gpt-4");
        Assert.Equal("gpt-4", request.Model);
        Assert.Equal(4, request.Messages.Count); // System message is excluded when using SystemPrompt
    }

    /// <summary>
    /// Tests function calling with mocked provider.
    /// </summary>
    [Fact]
    public Task FunctionCalling_ShouldWork()
    {
        var context = new ConversationContext();
        context.AvailableTools.Add(new ToolDeclaration
        {
            Name = "get_weather",
            Description = "Get the weather for a location",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["location"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "The city and state"
                    }
                },
                ["required"] = new[] { "location" }
            }
        });

        context.AddUserMessage("What's the weather in New York?");

        // Simulate assistant response with function call
        var functionCalls = new List<FunctionCall>
        {
            new FunctionCall
            {
                Id = "call_123",
                Name = "get_weather",
                Arguments = new Dictionary<string, object?>
                {
                    ["location"] = "New York, NY"
                }
            }
        };

        context.AddAssistantMessageWithToolCalls(null, functionCalls);

        // Add tool response
        context.AddToolResponse("get_weather", "call_123", new { temperature = 72, condition = "sunny" });

        Assert.Equal(3, context.Messages.Count); // User + Assistant with tool call + Tool response

        var toolMessage = context.Messages.Last();
        Assert.Equal(Role.Tool, toolMessage.Role);
        Assert.NotNull(toolMessage.ToolResults);
        Assert.Single(toolMessage.ToolResults);
        Assert.Equal("get_weather", toolMessage.ToolResults[0].Name);
        Assert.Equal("call_123", toolMessage.ToolResults[0].CallId);

        return Task.CompletedTask;
    }
}

/// <summary>
/// Test fixture for integration tests.
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public IntegrationTestFixture()
    {
        var services = new ServiceCollection();

        // Configure services
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Configure LLM services from environment
        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
        });

        _serviceProvider = services.BuildServiceProvider();
    }

    public ILlmProvider GetProvider(string name)
    {
        var factory = _serviceProvider.GetRequiredService<ILlmProviderFactory>();
        return factory.CreateProvider(name);
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}


/// <summary>
/// Attribute for skippable facts that can be conditionally skipped at runtime.
/// </summary>
public class SkippableFactAttribute : FactAttribute
{
    // Tests marked with this attribute will return early if conditions aren't met
}
