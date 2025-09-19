using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Llm.Examples.Shared;
/// <summary>
/// Example demonstrating Ollama local LLM integration with Andy.Llm
/// </summary>
class Program
{
    static async Task Main()
    {
        Console.WriteLine("=== Ollama Local LLM Example ===\n");

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

        // Get Ollama configuration
        var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434";
        var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");

        logger.LogInformation("Ollama configuration:");
        logger.LogInformation("  API Base: {ApiBase}", ollamaBase);

        try
        {
            // Show available models and get the first one if no model specified
            var firstAvailableModel = await ShowAvailableModels(ollamaBase, logger);

            if (string.IsNullOrEmpty(ollamaModel) && !string.IsNullOrEmpty(firstAvailableModel))
            {
                ollamaModel = firstAvailableModel;
                logger.LogInformation("No OLLAMA_MODEL environment variable set, using first available: {Model}", ollamaModel);
                Environment.SetEnvironmentVariable("OLLAMA_MODEL", ollamaModel);
            }

            if (string.IsNullOrEmpty(ollamaModel))
            {
                logger.LogError("No Ollama models found and OLLAMA_MODEL not set!");
                logger.LogError("\n❌ No models available in Ollama.");
                logger.LogError("\nTo install models:");
                logger.LogError("1. Make sure Ollama is running: ollama serve");
                logger.LogError("2. Pull a model: ollama pull llama2");
                logger.LogError("3. List installed models: ollama list");
                return;
            }

            logger.LogInformation("Using model: {Model}", ollamaModel);

            var factory = serviceProvider.GetRequiredService<Andy.Llm.Providers.ILlmProviderFactory>();
            var ollamaProvider = factory.CreateProvider("ollama");

            // Check if Ollama is running
            logger.LogInformation("\nChecking Ollama availability...");
            if (!await ollamaProvider.IsAvailableAsync())
            {
                logger.LogError("Ollama is not available!");
                logger.LogError("\n❌ Ollama is not running or not accessible.");
                logger.LogError("\nTo start Ollama:");
                logger.LogError("1. Install Ollama from https://ollama.ai");
                logger.LogError("2. Start the server: ollama serve");
                logger.LogError("\nAvailable models can be listed with: ollama list");
                return;
            }
            logger.LogInformation("✓ Ollama is available!");

            // Example 1: Simple completion
            await RunSimpleCompletion(ollamaProvider, logger);

            // Example 2: Conversation with context
            await RunConversationExample(ollamaProvider, logger);

            // Example 3: Streaming response
            await RunStreamingExample(ollamaProvider, logger);

            // Example 4: Code generation
            await RunCodeGenerationExample(ollamaProvider, logger);

            // Example 5: Performance metrics
            await RunPerformanceExample(ollamaProvider, logger);

            // Example 6: Different models comparison (if multiple models available)
            await RunModelComparisonExample(factory, logger);

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the Ollama demonstration");
        }
    }

    static async Task<string?> ShowAvailableModels(string apiBase, ILogger logger)
    {
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"{apiBase}/api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                logger.LogInformation("\nAvailable Ollama models:");

                // Parse JSON to extract model names
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var models = new List<string>();

                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var modelElement in modelsArray.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("name", out var nameElement))
                        {
                            var modelName = nameElement.GetString();
                            if (!string.IsNullOrEmpty(modelName))
                            {
                                models.Add(modelName);
                                logger.LogInformation("  - {ModelName}", modelName);
                            }
                        }
                    }
                }

                // Return the first available model
                return models.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug("Failed to get available models: {Message}", ex.Message);
        }

        return null;
    }

    static async Task RunSimpleCompletion(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 1: Simple Completion ---");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "What are the benefits of running AI models locally?" }
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
            new Message { Role = Role.System, Content = "You are a helpful AI assistant running locally. Be concise and technical." },
            new Message { Role = Role.User, Content = "What is Ollama?" },
            new Message { Role = Role.Assistant, Content = "Ollama is an open-source tool that allows you to run large language models locally on your machine. It provides a simple API and CLI for managing and running models like Llama 2, Mistral, and others." },
            new Message { Role = Role.User, Content = "What hardware do I need?" }
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
        Console.WriteLine("\nGenerating a haiku about local AI (streaming):\n");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Write a haiku about running AI models locally." }
            },
            Config = new LlmClientConfig
            {
                MaxTokens = 100,
                Temperature = 0.9m
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
                Console.WriteLine("\n\n[Stream complete]");
            }
        }
    }

    static async Task RunCodeGenerationExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 4: Code Generation ---");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.System, Content = "You are a code assistant. Generate clean, commented code." },
                new Message { Role = Role.User, Content = "Write a Python function to calculate factorial recursively." }
            },
            Config = new LlmClientConfig
            {
                MaxTokens = 200,
                Temperature = 0.3m // Lower temperature for more deterministic code
            }
        };

        var response = await provider.CompleteAsync(request);
        Console.WriteLine($"\nGenerated Code:\n{response.Content}");
    }

    static async Task RunPerformanceExample(Andy.Llm.Providers.ILlmProvider provider, ILogger logger)
    {
        logger.LogInformation("\n--- Example 5: Performance Metrics ---");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "Count from 1 to 5." }
            },
            Config = new LlmClientConfig { MaxTokens = 50 }
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await provider.CompleteAsync(request);
        stopwatch.Stop();

        Console.WriteLine($"\nResponse: {response.Content}");
        Console.WriteLine($"\nPerformance Metrics:");
        Console.WriteLine($"  Response time: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"  Tokens generated: {response.Usage?.TotalTokens}");

        if (response.Usage?.TotalTokens > 0 && stopwatch.ElapsedMilliseconds > 0)
        {
            var tokensPerSecond = (response.Usage.TotalTokens * 1000.0) / stopwatch.ElapsedMilliseconds;
            Console.WriteLine($"  Tokens/second: {tokensPerSecond:F2}");
        }

        // Display Ollama-specific metadata if available
        if (response.Metadata != null)
        {
            Console.WriteLine("\nOllama Timing Details:");
            foreach (var kvp in response.Metadata)
            {
                if (kvp.Key.Contains("duration"))
                {
                    // Convert nanoseconds to milliseconds
                    if (kvp.Value is long nanos)
                    {
                        var ms = nanos / 1_000_000.0;
                        Console.WriteLine($"  {kvp.Key}: {ms:F2}ms");
                    }
                }
            }
        }
    }

    static async Task RunModelComparisonExample(Andy.Llm.Providers.ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n--- Example 6: Model Comparison ---");
        logger.LogInformation("This example compares different models if you have multiple installed.");

        // Get list of actually available models
        var availableModels = new List<string>();
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync($"{Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434"}/api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    foreach (var modelElement in modelsArray.EnumerateArray())
                    {
                        if (modelElement.TryGetProperty("name", out var nameElement))
                        {
                            var modelName = nameElement.GetString();
                            if (!string.IsNullOrEmpty(modelName))
                            {
                                availableModels.Add(modelName);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall back to trying common models
            availableModels = new List<string> { "llama2", "mistral", "codellama" };
        }

        if (availableModels.Count == 0)
        {
            logger.LogWarning("No models found for comparison");
            return;
        }

        var models = availableModels.Take(3).ToArray(); // Compare up to 3 models
        var prompt = "Explain quantum computing in one sentence.";

        foreach (var model in models)
        {
            try
            {
                // Try to use different models by setting environment variable
                Environment.SetEnvironmentVariable("OLLAMA_MODEL", model);
                var provider = factory.CreateProvider("ollama");

                logger.LogInformation("\nTrying model: {Model}", model);

                var request = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message { Role = Role.User, Content = prompt }
                    },
                    Config = new LlmClientConfig
                    {
                        Model = model,
                        MaxTokens = 100,
                        Temperature = 0.7m
                    }
                };

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await provider.CompleteAsync(request);
                stopwatch.Stop();

                Console.WriteLine($"\n{model} response ({stopwatch.ElapsedMilliseconds}ms):");
                Console.WriteLine(response.Content);
            }
            catch (Exception ex)
            {
                logger.LogDebug($"Model {model} not available: {ex.Message}");
            }
        }

        // Reset environment variable to the original or first available
        var originalModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? availableModels.FirstOrDefault();
        if (!string.IsNullOrEmpty(originalModel))
        {
            Environment.SetEnvironmentVariable("OLLAMA_MODEL", originalModel);
        }
    }
}
