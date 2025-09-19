using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Llm.Examples.Shared;
/// <summary>
/// Example demonstrating Azure OpenAI Service integration with Andy.Llm
/// </summary>
class Program
{
    static async Task Main()
    {
        Console.WriteLine("=== Azure OpenAI Service Example ===\n");

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Configure services
        var services = new ServiceCollection();

        // Add logging with clean console output
        services.AddLogging(builder => builder.AddCleanConsole());

        // Add LLM services with configuration
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        // Check for Azure configuration
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
        var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

        if (string.IsNullOrEmpty(azureEndpoint) || string.IsNullOrEmpty(azureKey))
        {
            logger.LogError("❌ Azure OpenAI configuration missing!");
            Console.WriteLine("\nPlease set the following environment variables:");
            Console.WriteLine("  AZURE_OPENAI_ENDPOINT - Your Azure OpenAI endpoint (e.g., https://myresource.openai.azure.com)");
            Console.WriteLine("  AZURE_OPENAI_KEY - Your Azure OpenAI API key");
            Console.WriteLine("  AZURE_OPENAI_DEPLOYMENT - Your deployment name (e.g., gpt-4)");
            Console.WriteLine("\nOptional:");
            Console.WriteLine("  AZURE_OPENAI_API_VERSION - API version (default: 2024-02-15-preview)");
            return;
        }

        logger.LogInformation("Azure OpenAI configured:");
        logger.LogInformation("  Endpoint: {Endpoint}", azureEndpoint);
        logger.LogInformation("  Deployment: {Deployment}", azureDeployment ?? "default");

        try
        {
            var factory = serviceProvider.GetRequiredService<Andy.Llm.Providers.ILlmProviderFactory>();
            var azureProvider = factory.CreateProvider("azure");

            // Check availability
            logger.LogInformation("\nChecking Azure OpenAI availability...");
            if (!await azureProvider.IsAvailableAsync())
            {
                logger.LogError("❌ Azure OpenAI is not available. Please check your configuration.");
                return;
            }
            logger.LogInformation("✓ Azure OpenAI is available!");

            // Example 1: Simple completion
            await RunSimpleCompletion(azureProvider, logger);

            // Example 2: Conversation with system prompt
            await RunConversationExample(azureProvider, logger);

            // Example 3: Streaming response
            await RunStreamingExample(azureProvider, logger);

            // Example 4: Function calling (if supported by deployment)
            await RunFunctionCallingExample(azureProvider, logger);

            // Example 5: Token usage tracking
            await RunTokenUsageExample(azureProvider, logger);

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the Azure OpenAI demonstration");
        }
    }

    static async Task RunSimpleCompletion(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 1: Simple Completion ---");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "What is Azure OpenAI Service and what are its key benefits?" }
            },
            Config = new LlmClientConfig
            {
                MaxTokens = 200,
                Temperature = 0.7m
            }
        };

        var response = await provider.CompleteAsync(request);
        Console.WriteLine($"\nResponse: {response.Content}");
        Console.WriteLine($"Tokens used: {response.Usage?.TotalTokens}");
    }

    static async Task RunConversationExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 2: Conversation with Context ---");

        var messages = new List<Message>
        {
            new Message { Role = Role.System, Content = "You are an Azure cloud architecture expert. Provide concise, technical responses." },
            new Message { Role = Role.User, Content = "What's the difference between Azure OpenAI and OpenAI's API?" },
            new Message { Role = Role.Assistant, Content = "Azure OpenAI provides the same models as OpenAI but with enterprise features like private endpoints, managed identity, content filtering, and compliance certifications. It's integrated with Azure's security and runs in your Azure subscription." },
            new Message { Role = Role.User, Content = "What about data privacy?" }
        };

        var request = new LlmRequest
        {
            Messages = messages,
            Config = new LlmClientConfig { MaxTokens = 150 }
        };

        var response = await provider.CompleteAsync(request);
        Console.WriteLine($"\nResponse: {response.Content}");
    }

    static async Task RunStreamingExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 3: Streaming Response ---");
        Console.WriteLine("\nGenerating Azure best practices (streaming):\n");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "List 5 Azure OpenAI Service best practices, one per line." }
            },
            Config = new LlmClientConfig
            {
                MaxTokens = 200,
                Temperature = 0.5m
            }
        };

        await foreach (var chunk in provider.StreamCompleteAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                Console.Write(chunk.TextDelta);
            }
            if (chunk.IsComplete)
            {
                Console.WriteLine("\n[Stream complete]");
            }
        }
    }

    static async Task RunFunctionCallingExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 4: Function Calling ---");
        logger.LogInformation("Note: Function calling requires a deployment that supports it (e.g., gpt-4)");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "What's the current status of my Azure subscription with ID 'sub-12345'?" }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "get_subscription_status",
                    Description = "Get the status of an Azure subscription",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["subscription_id"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The Azure subscription ID"
                            }
                        },
                        ["required"] = new[] { "subscription_id" }
                    }
                }
            },
            Config = new LlmClientConfig { MaxTokens = 150 }
        };

        try
        {
            var response = await provider.CompleteAsync(request);

            if (response.ToolCalls.Any())
            {
                logger.LogInformation("Function call detected:");
                foreach (var call in response.ToolCalls)
                {
                    Console.WriteLine($"  Function: {call.Name}");
                    Console.WriteLine($"  Arguments: {call.ArgumentsJson}");
                }
            }
            else
            {
                Console.WriteLine($"Response: {response.Content}");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Function calling may not be supported by this deployment: {Message}", ex.Message);
        }
    }

    static async Task RunTokenUsageExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 5: Token Usage Tracking ---");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Explain Azure regions in one sentence." }
            },
            Config = new LlmClientConfig { MaxTokens = 50 }
        };

        var response = await provider.CompleteAsync(request);

        Console.WriteLine($"\nResponse: {response.Content}");
        Console.WriteLine("\nToken Usage Details:");
        Console.WriteLine($"  Prompt tokens: {response.Usage?.PromptTokens}");
        Console.WriteLine($"  Completion tokens: {response.Usage?.CompletionTokens}");
        Console.WriteLine($"  Total tokens: {response.Usage?.TotalTokens}");

        // Calculate approximate cost (example rates, check Azure pricing)
        if (response.Usage != null)
        {
            var promptCost = (response.Usage.PromptTokens / 1000.0) * 0.03;  // Example: $0.03 per 1K tokens
            var completionCost = (response.Usage.CompletionTokens / 1000.0) * 0.06;  // Example: $0.06 per 1K tokens
            Console.WriteLine($"\nEstimated cost (example rates):");
            Console.WriteLine($"  Prompt: ${promptCost:F4}");
            Console.WriteLine($"  Completion: ${completionCost:F4}");
            Console.WriteLine($"  Total: ${promptCost + completionCost:F4}");
        }
    }
}
