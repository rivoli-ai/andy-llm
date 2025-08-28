using Andy.Llm;
using Andy.Llm.Extensions;
using Andy.Llm.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating streaming responses with Andy.Llm
/// </summary>
public class StreamingExample
{
    public static async Task Main(string[] args)
    {
        // Setup
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = false;
                options.SingleLine = true;
                options.TimestampFormat = "";
            });
            builder.SetMinimumLevel(LogLevel.Information);
            // Hide HTTP client and provider logs
            builder.AddFilter("System.Net.Http", LogLevel.Warning);
            builder.AddFilter("Andy.Llm.Providers", LogLevel.Warning);
            builder.AddFilter("Andy.Llm.Services", LogLevel.Warning);
        });
        
        // Configure LLM services
        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
        });
        
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
            
            var llmClient = serviceProvider.GetRequiredService<LlmClient>();

            // Example 1: Basic streaming
            await BasicStreaming(llmClient, logger);

            // Example 2: Streaming with cancellation
            await StreamingWithCancellation(llmClient, logger);

            // Example 3: Streaming with progress display
            await StreamingWithProgress(llmClient, logger);

            // Example 4: Streaming with error handling
            await StreamingWithErrorHandling(llmClient, logger);

            // Example 5: Streaming function calls
            await StreamingWithFunctionCalls(llmClient, logger);

            logger.LogInformation("\nStreaming examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during streaming examples");
        }
    }

    static async Task BasicStreaming(LlmClient client, ILogger logger)
    {
        logger.LogInformation("\n=== Example 1: Basic Streaming ===");
        
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Write a short poem about programming.")
            },
            Model = "gpt-4o-mini",
            Stream = true
        };

        logger.LogInformation("Streaming response:");
        try
        {
            await foreach (var chunk in client.StreamCompleteAsync(request))
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

    static async Task StreamingWithCancellation(LlmClient client, ILogger logger)
    {
        logger.LogInformation("\n=== Example 2: Streaming with Cancellation ===");
        
        using var cts = new CancellationTokenSource();
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Count from 1 to 100 slowly, one number at a time.")
            },
            Model = "gpt-4o-mini",
            Stream = true
        };

        logger.LogInformation("Streaming (will cancel after 2 seconds):");
        
        // Cancel after 2 seconds
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        
        try
        {
            await foreach (var chunk in client.StreamCompleteAsync(request, cts.Token))
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

    static async Task StreamingWithProgress(LlmClient client, ILogger logger)
    {
        logger.LogInformation("\n=== Example 3: Streaming with Progress ===");
        
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "List 5 programming languages with brief descriptions.")
            },
            Model = "gpt-4o-mini",
            Stream = true
        };

        logger.LogInformation("Streaming response with character count:");
        
        int totalChars = 0;
        int chunkCount = 0;
        
        try
        {
            await foreach (var chunk in client.StreamCompleteAsync(request))
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

    static async Task StreamingWithErrorHandling(LlmClient client, ILogger logger)
    {
        logger.LogInformation("\n=== Example 4: Streaming with Error Handling ===");
        
        // Intentionally use a very long prompt that might cause issues
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Generate a simple greeting.")
            },
            Model = "gpt-4o-mini",
            Stream = true,
            MaxTokens = 10  // Very low token limit to demonstrate handling
        };

        logger.LogInformation("Streaming with error handling:");
        
        try
        {
            await foreach (var chunk in client.StreamCompleteAsync(request))
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

    static async Task StreamingWithFunctionCalls(LlmClient client, ILogger logger)
    {
        logger.LogInformation("\n=== Example 5: Streaming with Complex Content ===");
        logger.LogInformation("Demonstrating streaming with a more complex request\n");
        
        // Note: Function calling in streaming mode is not always supported
        // We'll use a regular streaming request instead
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.System, "You are a helpful assistant that explains things step by step."),
                Message.CreateText(MessageRole.User, "Calculate 15% of 240 and show your work.")
            },
            Model = "gpt-4o-mini",
            Stream = true,
            MaxTokens = 200
        };
        
        logger.LogInformation("Streaming response with step-by-step calculation:");
        
        try
        {
            int chunkCount = 0;
            
            await foreach (var chunk in client.StreamCompleteAsync(request))
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