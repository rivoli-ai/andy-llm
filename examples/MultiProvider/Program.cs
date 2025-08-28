using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Abstractions;
using Andy.Llm.Services;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating multiple provider support
/// </summary>
public class MultiProvider
{
    public static async Task Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Configure multiple providers
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            
            // OpenAI configuration
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                Model = "gpt-4o-mini",
                Enabled = true
            };
            
            // Cerebras configuration
            options.Providers["cerebras"] = new ProviderConfig
            {
                ApiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY"),
                Model = "llama3.1-8b",
                Enabled = true
            };
            
            // You can add more providers here...
        });

        var serviceProvider = services.BuildServiceProvider();
        var providerFactory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
        var logger = serviceProvider.GetRequiredService<ILogger<MultiProvider>>();

        Console.WriteLine("Multi-Provider Example");
        Console.WriteLine("======================");
        Console.WriteLine("Available commands:");
        Console.WriteLine("  /provider <name> - Switch provider (openai, cerebras)");
        Console.WriteLine("  /test - Test all configured providers");
        Console.WriteLine("  exit - Quit");
        Console.WriteLine();

        var currentProviderName = "openai";
        ILlmProvider currentProvider = null;

        try
        {
            currentProvider = providerFactory.CreateProvider(currentProviderName);
            Console.WriteLine($"Using provider: {currentProviderName}\n");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create default provider");
            Console.WriteLine($"Failed to create default provider. Trying to find available provider...");
            
            try
            {
                currentProvider = await providerFactory.CreateAvailableProviderAsync();
                currentProviderName = currentProvider.Name;
                Console.WriteLine($"Using fallback provider: {currentProviderName}\n");
            }
            catch
            {
                Console.WriteLine("No providers available. Please check your configuration.");
                return;
            }
        }

        while (true)
        {
            Console.Write($"[{currentProviderName}] You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (input.StartsWith("/provider ", StringComparison.OrdinalIgnoreCase))
            {
                var newProvider = input.Substring("/provider ".Length).Trim();
                try
                {
                    currentProvider = providerFactory.CreateProvider(newProvider);
                    currentProviderName = newProvider;
                    Console.WriteLine($"Switched to provider: {currentProviderName}\n");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to switch provider: {ex.Message}\n");
                }
                continue;
            }

            if (input.Equals("/test", StringComparison.OrdinalIgnoreCase))
            {
                await TestAllProviders(providerFactory, logger);
                continue;
            }

            // Create request
            var request = new LlmRequest
            {
                Messages = new List<Message>
                {
                    Message.CreateText(MessageRole.User, input)
                },
                MaxTokens = 500,
                Temperature = 0.7
            };

            try
            {
                Console.Write($"[{currentProviderName}] Assistant: ");
                
                // Stream the response
                var fullResponse = "";
                await foreach (var chunk in currentProvider.StreamCompleteAsync(request))
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        Console.Write(chunk.TextDelta);
                        fullResponse += chunk.TextDelta;
                    }
                }
                Console.WriteLine("\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\n");
                logger.LogError(ex, "Provider error");
            }
        }

        Console.WriteLine("Goodbye!");
    }

    private static async Task TestAllProviders(ILlmProviderFactory factory, ILogger logger)
    {
        Console.WriteLine("\nTesting all configured providers...");
        Console.WriteLine("===================================");

        var providers = new[] { "openai", "cerebras", "azure", "ollama" };
        var testRequest = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Say 'Hello' and nothing else.")
            },
            MaxTokens = 10,
            Temperature = 0
        };

        foreach (var providerName in providers)
        {
            Console.Write($"Testing {providerName}... ");
            
            try
            {
                var provider = factory.CreateProvider(providerName);
                
                // Check if available
                var isAvailable = await provider.IsAvailableAsync();
                if (!isAvailable)
                {
                    Console.WriteLine("Not available");
                    continue;
                }

                // Try a simple completion
                var response = await provider.CompleteAsync(testRequest);
                if (!string.IsNullOrEmpty(response.Content))
                {
                    Console.WriteLine($"✓ Working (Response: {response.Content.Trim()})");
                }
                else
                {
                    Console.WriteLine("✗ No response");
                }
            }
            catch (NotSupportedException)
            {
                Console.WriteLine("Not configured");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error: {ex.Message}");
                logger.LogDebug(ex, "Provider test failed for {Provider}", providerName);
            }
        }

        Console.WriteLine("\nTest complete.\n");
    }
}