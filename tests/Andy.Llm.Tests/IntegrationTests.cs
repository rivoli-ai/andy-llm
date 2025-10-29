using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Tests;

/// <summary>
/// Integration tests for LLM providers.
/// These tests require environment variables to be set for the respective providers.
/// </summary>
[Collection("EnvironmentVariable Tests")]  // Prevent parallel execution with ConfigurationPriorityTests
public class IntegrationTests : IClassFixture<IntegrationTests.IntegrationTestFixture>
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
                new Message {Role = Role.User, Content = "Say 'Hello, World!' and nothing else."}
            },
            Config = new LlmClientConfig
            {
                Model = "gpt-4o-mini",
                MaxTokens = 50,
                Temperature = 0
            }
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests OpenAI provider with gpt-5-codex model.
    /// </summary>
    [SkippableFact]
    public async Task OpenAI_Gpt5Codex_ShouldWork()
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
                new Message {Role = Role.User, Content = "Say 'Hello, World!' and nothing else."}
            },
            Config = new LlmClientConfig
            {
                Model = "gpt-5-codex",
                MaxTokens = 50,
                Temperature = 0
            }
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.Content, StringComparison.OrdinalIgnoreCase);
        // Verify that model information is captured
        Assert.NotNull(response.Model);
        Assert.NotEmpty(response.Model);
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
                new Message {Role = Role.User, Content = "Say 'Hello, World!' and nothing else."}
            },
            Config = new LlmClientConfig
            {
                Model = "llama3.1-8b",
                MaxTokens = 50,
                Temperature = 0
            }
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests Cerebras function calling with llama-3.3-70b.
    /// </summary>
    [SkippableFact]
    public async Task Cerebras_FunctionCalling_WithLlama33_ShouldWork()
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
                new Message { Role = Role.User, Content = "What's the weather in San Francisco?" }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "get_weather",
                    Description = "Get the current weather for a location",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["location"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The city and state, e.g. San Francisco, CA"
                            }
                        },
                        ["required"] = new[] { "location" }
                    }
                }
            },
            Config = new LlmClientConfig
            {
                Model = "llama-3.3-70b",  // Only this model supports function calling
                MaxTokens = 100,
                Temperature = 0
            }
        };

        var response = await provider.CompleteAsync(request);

        Assert.NotNull(response);
        // Should either call the tool or explain it can't
        Assert.True(response.HasToolCalls || !string.IsNullOrEmpty(response.Content),
            "Response should either have tool calls or content");

        if (response.HasToolCalls)
        {
            Assert.NotEmpty(response.ToolCalls);
            Assert.Contains(response.ToolCalls, tc => tc.Name == "get_weather");
        }
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
            Messages = new List<Message> {new Message {Role = Role.User, Content = "Count from 1 to 5."}},
            Config = new LlmClientConfig
            {
                Model = "gpt-4o-mini",
                MaxTokens = 100,
                Temperature = 0
            }
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
            services.AddLlmServices(options =>
            {
                options.DefaultProvider = "openai";

                // Provide default configuration that will be merged with environment variables
                options.Providers["openai"] = new Configuration.ProviderConfig
                {
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4o-mini"
                };

                options.Providers["cerebras"] = new Configuration.ProviderConfig
                {
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama3.1-8b"
                };
            });

            // Environment variables will override the above defaults
            services.ConfigureLlmFromEnvironment();

            _serviceProvider = services.BuildServiceProvider();
        }

        public Andy.Model.Llm.ILlmProvider GetProvider(string name)
        {
            var factory = _serviceProvider.GetRequiredService<ILlmProviderFactory>();
            return factory.CreateProvider(name);
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }
}

/// <summary>
/// Attribute for skippable facts that can be conditionally skipped at runtime.
/// </summary>
public class SkippableFactAttribute : FactAttribute
{
    // Tests marked with this attribute will return early if conditions aren't met
}
