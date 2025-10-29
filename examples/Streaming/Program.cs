using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Llm.Examples.Shared;
/// <summary>
/// Example demonstrating streaming responses with Andy.Llm
/// </summary>
public class StreamingExample
{
    public static async Task Main()
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        // Setup
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddCleanConsole());

        // Configure LLM services from appsettings.json, then environment variables will merge
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<StreamingExample>>();

        try
        {
            logger.LogInformation("=== Streaming Examples ===\n");
            logger.LogInformation("Starting streaming demonstrations with OpenAI...\n");

            // Check for API key
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            {
                logger.LogError("OPENAI_API_KEY environment variable is not set!");
                logger.LogError("Please set your OpenAI API key:");
                logger.LogError("  export OPENAI_API_KEY=sk-...");
                return;
            }

            var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
            var llmProvider = await factory.CreateAvailableProviderAsync();

            logger.LogInformation("Using provider: {Provider}\n", llmProvider.Name);

            // Example 1: Basic streaming
            await BasicStreaming(llmProvider, logger);

            // Example 2: Streaming with cancellation
            await StreamingWithCancellation(llmProvider, logger);

            // Example 3: Streaming with progress display
            await StreamingWithProgress(llmProvider, logger);

            // Example 4: Streaming with error handling
            await StreamingWithErrorHandling(llmProvider, logger);

            // Example 5: Streaming function calls
            await StreamingWithFunctionCalls(llmProvider, logger);

            logger.LogInformation("\nStreaming examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during streaming examples");
        }
    }

    static async Task BasicStreaming(Andy.Model.Llm.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 1: Basic Streaming ===");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Write a short poem about programming." }
            },
            Config = new LlmClientConfig { Model = "gpt-4o-mini",
            MaxTokens = 1000 }
        };

        logger.LogInformation("Streaming response:");
        try
        {
            await foreach (var chunk in provider.StreamCompleteAsync(request))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                }

                if (chunk.Error != null)
                {
                    logger.LogError("\nError: {Error}", chunk.Error);
                    break;
                }

                if (chunk.IsComplete)
                {
                    logger.LogInformation("\n[Stream complete]");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Streaming failed: {Message}", ex.Message);
        }
    }

    static async Task StreamingWithCancellation(Andy.Model.Llm.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 2: Streaming with Cancellation ===");

        using var cts = new CancellationTokenSource();
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Count from 1 to 100 slowly, one number at a time." }
            },
            Config = new LlmClientConfig { Model = "gpt-4o-mini",
            MaxTokens = 1000 }
        };

        logger.LogInformation("Streaming (will cancel after 2 seconds):");

        // Cancel after 2 seconds
        cts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            await foreach (var chunk in provider.StreamCompleteAsync(request, cts.Token))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("\n[Stream cancelled]");
        }
        catch (Exception ex)
        {
            logger.LogError("Streaming failed: {Message}", ex.Message);
        }
    }

    static async Task StreamingWithProgress(Andy.Model.Llm.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 3: Streaming with Progress ===");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "List 5 programming languages with brief descriptions." }
            },
            Config = new LlmClientConfig { Model = "gpt-4o-mini",
            MaxTokens = 1000 }
        };

        logger.LogInformation("Streaming response with character count:");

        int totalChars = 0;
        int chunkCount = 0;

        try
        {
            await foreach (var chunk in provider.StreamCompleteAsync(request))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                    totalChars += chunk.TextDelta.Length;
                    chunkCount++;
                }

                if (chunk.IsComplete)
                {
                    logger.LogInformation("\n[Stream complete: {TotalChars} characters in {ChunkCount} chunks]",
                        totalChars, chunkCount);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Streaming failed: {Message}", ex.Message);
        }
    }

    static async Task StreamingWithErrorHandling(Andy.Model.Llm.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 4: Streaming with Error Handling ===");

        // Intentionally use a very long prompt that might cause issues
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Generate a simple greeting." }
            },
            Config = new LlmClientConfig { Model = "gpt-4o-mini", MaxTokens = 10 }  // Very low token limit to demonstrate handling
        };

        logger.LogInformation("Streaming with error handling:");

        try
        {
            await foreach (var chunk in provider.StreamCompleteAsync(request))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                }

                if (chunk.Error != null)
                {
                    logger.LogError("\nStream error: {Error}", chunk.Error);
                    break;
                }

                // Check if output was truncated (would need FinishReason in streaming)
                // This information may not be available in streaming chunks

                if (chunk.IsComplete)
                {
                    logger.LogInformation("\n[Stream complete]");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Streaming failed: {Message}", ex.Message);
            logger.LogDebug(ex, "Full error details");
        }
    }

    static async Task StreamingWithFunctionCalls(Andy.Model.Llm.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 5: Streaming with Complex Content ===");
        logger.LogInformation("Demonstrating streaming with a more complex request\n");

        // Note: Function calling in streaming mode is not always supported
        // We'll use a regular streaming request instead
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.System, Content = "You are a helpful assistant that explains things step by step." },
                new Message { Role = Role.User, Content = "Calculate 15% of 240 and show your work." }
            },
            Config = new LlmClientConfig { Model = "gpt-4o-mini", MaxTokens = 200 }
        };

        logger.LogInformation("Streaming response with step-by-step calculation:");

        try
        {
            int chunkCount = 0;

            await foreach (var chunk in provider.StreamCompleteAsync(request))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                    chunkCount++;
                }

                if (chunk.Error != null)
                {
                    logger.LogError("\nStream error: {Error}", chunk.Error);
                    break;
                }

                if (chunk.IsComplete)
                {
                    logger.LogInformation("\n[Stream complete - {ChunkCount} chunks received]", chunkCount);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Streaming with functions failed: {Message}", ex.Message);
            logger.LogDebug(ex, "Full error details");
        }
    }
}
