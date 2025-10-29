using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating multiple provider support
/// </summary>
public class MultiProvider
{
    public static async Task Main()
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // Setup dependency injection
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddCleanConsole());

        // Configure multiple providers from appsettings.json, then environment variables will merge
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<MultiProvider>>();

        try
        {
            var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

            logger.LogInformation("=== Multi-Provider Example ===\n");

            // Example 1: Use default provider
            await UseDefaultProvider(factory, logger);

            // Example 2: Use specific providers
            await UseSpecificProviders(factory, logger);

            // Example 3: Provider fallback
            await DemonstrateProviderFallback(factory, logger);

            // Example 4: Compare providers
            await CompareProviders(factory, logger);

            // Example 5: Provider-specific features
            await ProviderSpecificFeatures(factory, logger);

            logger.LogInformation("\nMulti-provider examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during multi-provider examples");
        }
    }

    static async Task UseDefaultProvider(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== Example 1: Using Default Provider ===");

        try
        {
            var provider = await factory.CreateAvailableProviderAsync(); // Uses default from config
            logger.LogInformation("Default provider: {ProviderName}", provider.Name);

            var request = new LlmRequest
            {
                Messages = new List<Message>
                {
                    new Message { Role = Role.User, Content = "Say hello in one word." }
                },
                Config = new LlmClientConfig { MaxTokens = 10 }
            };

            var response = await provider.CompleteAsync(request);
            logger.LogInformation("Response: {Response}", response.Content);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Default provider failed: {Message}", ex.Message);
        }
    }

    static async Task UseSpecificProviders(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== Example 2: Using Specific Providers ===");
        logger.LogInformation("Note: Cerebras runs open-source models like Llama (Meta) at high speed");

        var providers = new[] { "openai/latest-small", "cerebras/fast-large", "azure/production", "ollama/local" };

        foreach (var providerName in providers)
        {
            try
            {
                var provider = factory.CreateProvider(providerName);

                if (!await provider.IsAvailableAsync())
                {
                    logger.LogWarning("{Provider} is not available", providerName);
                    continue;
                }

                logger.LogInformation("\nUsing {Provider}:", providerName);

                var request = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message { Role = Role.User, Content = $"Complete this: 'The sky is' (3 words)" }
                    },
                    Config = new LlmClientConfig
                    {
                        MaxTokens = 50
                    }
                };

                var response = await provider.CompleteAsync(request);
                if (string.IsNullOrEmpty(response.Content))
                {
                    logger.LogWarning("  Response is empty! Model: {Model}", request.Model ?? "default");
                }
                logger.LogInformation("  Response: {Response}", response.Content);

                if (response.Usage != null)
                {
                    logger.LogInformation("  Tokens used: {Tokens}", response.Usage.TotalTokens);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Provider {Provider} failed: {Message}", providerName, ex.Message);
            }
        }
    }

    static async Task DemonstrateProviderFallback(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== Example 3: Provider Fallback ===");

        try
        {
            // This will automatically find the first available provider
            var provider = await factory.CreateAvailableProviderAsync(CancellationToken.None);
            logger.LogInformation("Found available provider: {Provider}", provider.Name);

            var request = new LlmRequest
            {
                Messages = new List<Message>
                {
                    new Message { Role = Role.User, Content = "What's 2+2?" }
                },
                Config = new LlmClientConfig { MaxTokens = 10 }
            };

            var response = await provider.CompleteAsync(request);
            logger.LogInformation("Response: {Response}", response.Content);
        }
        catch (Exception ex)
        {
            logger.LogError("Provider fallback failed: {Message}", ex.Message);
        }
    }

    static async Task CompareProviders(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== Example 4: Compare Provider Performance ===");

        var prompt = "Explain recursion in one sentence.";
        var providers = new[] { "openai/latest-small", "cerebras/fast-large" };

        foreach (var providerName in providers)
        {
            try
            {
                var provider = factory.CreateProvider(providerName);

                if (!await provider.IsAvailableAsync())
                {
                    logger.LogWarning("{Provider} not available for comparison", providerName);
                    continue;
                }

                logger.LogInformation("\n{Provider} response:", providerName);

                var request = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message { Role = Role.User, Content = prompt }
                    },
                    Config = new LlmClientConfig
                    {
                        MaxTokens = 100,
                        Temperature = 0.7m
                    }
                };

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await provider.CompleteAsync(request);
                stopwatch.Stop();

                logger.LogInformation("  Response: {Response}", response.Content);
                logger.LogInformation("  Time: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

                if (response.Usage != null)
                {
                    logger.LogInformation("  Tokens: {Tokens}", response.Usage.TotalTokens);
                    var tokensPerSecond = response.Usage.TotalTokens * 1000.0 / stopwatch.ElapsedMilliseconds;
                    logger.LogInformation("  Speed: {TokensPerSecond:F1} tokens/sec", tokensPerSecond);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Provider {Provider} comparison failed: {Message}",
                    providerName, ex.Message);
            }
        }
    }

    static async Task ProviderSpecificFeatures(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== Example 5: Provider-Specific Features ===");

        // OpenAI - Function Calling
        try
        {
            logger.LogInformation("\nOpenAI - Function Calling:");
            var openai = factory.CreateProvider("openai/latest-large");

            if (await openai.IsAvailableAsync())
            {
                // var conversation = new Conversation();
                // var contextManager = new ContextManager(conversation);
                var systemInstruction = "You are a helpful assistant.";
                var availableTools = new List<ToolDeclaration>
                {
                    new ToolDeclaration
                    {
                        Name = "get_time",
                        Description = "Get the current time",
                        Parameters = new Dictionary<string, object>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object>()
                        }
                    }
                };

                // Simplified without non-existent classes
                var messages = new List<Message>
                {
                    new Message { Role = Role.System, Content = systemInstruction },
                    new Message { Role = Role.User, Content = "What time is it?" }
                };

                var request = new LlmRequest
                {
                    Messages = messages,
                    Config = new LlmClientConfig { MaxTokens = 500 },
                    SystemPrompt = systemInstruction,
                    Tools = availableTools
                };

                var response = await openai.CompleteAsync(request);

                if (response.ToolCalls != null && response.ToolCalls.Any())
                {
                    logger.LogInformation("  Function call requested: {Function}",
                        response.ToolCalls.First().Name);
                }
                else
                {
                    logger.LogInformation("  Response: {Response}", response.Content);
                }
            }
            else
            {
                logger.LogWarning("OpenAI not available");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("OpenAI feature demo failed: {Message}", ex.Message);
        }

        // Cerebras - Fast Inference
        try
        {
            logger.LogInformation("\nCerebras - Fast Inference (using Llama models):");
            var cerebras = factory.CreateProvider("cerebras/fast-large");

            if (await cerebras.IsAvailableAsync())
            {
                var request = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message { Role = Role.User, Content = "Generate a list of 10 random numbers between 1 and 100." }
                    },
                    Config = new LlmClientConfig { MaxTokens = 200, Temperature = 0.9m }
                };

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await cerebras.CompleteAsync(request);
                stopwatch.Stop();

                logger.LogInformation("  Response length: {Length} chars", response.Content.Length);
                logger.LogInformation("  Generation time: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                logger.LogInformation("  (Cerebras is optimized for speed)");
            }
            else
            {
                logger.LogWarning("Cerebras not available");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Cerebras feature demo failed: {Message}", ex.Message);
        }

        // Ollama - Local Models
        try
        {
            logger.LogInformation("\nOllama - Local Models:");
            var ollama = factory.CreateProvider("ollama/local");

            if (await ollama.IsAvailableAsync())
            {
                var request = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message { Role = Role.User, Content = "What are the benefits of running models locally?" }
                    },
                    Config = new LlmClientConfig { MaxTokens = 100 }
                };

                var response = await ollama.CompleteAsync(request);
                if (!string.IsNullOrEmpty(response.Content))
                {
                    var truncated = response.Content.Length > 100
                        ? response.Content.Substring(0, 100) + "..."
                        : response.Content;
                    logger.LogInformation("  Response: {Response}", truncated);
                }
                else
                {
                    logger.LogWarning("  Ollama returned empty response");
                }
                logger.LogInformation("  (Running locally - no API costs!)");
            }
            else
            {
                logger.LogWarning("Ollama not available - is it running locally?");
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Ollama feature demo failed: {Message}", ex.Message);
        }
    }
}
