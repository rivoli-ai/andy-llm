using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Simple chat example using the Andy.Llm library
/// </summary>
public class SimpleChat
{
    public static async Task Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Configure LLM services from environment variables
        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(options =>
        {
            // Set default provider (can be overridden by environment variables)
            options.DefaultProvider = "openai";
            options.DefaultModel = "gpt-4o-mini";
            options.DefaultTemperature = 0.7;
            options.DefaultMaxTokens = 2000;
        });

        var serviceProvider = services.BuildServiceProvider();

        // Get the LLM client
        var llmClient = serviceProvider.GetRequiredService<LlmClient>();

        // Create a conversation context
        var context = new ConversationContext
        {
            SystemInstruction = "You are a helpful assistant. Be concise but informative."
        };

        Console.WriteLine("Simple Chat Example");
        Console.WriteLine("==================");
        Console.WriteLine("Type 'exit' to quit, 'clear' to clear context");
        Console.WriteLine();

        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                context.Clear();
                Console.WriteLine("Context cleared.\n");
                continue;
            }

            // Add user message to context
            context.AddUserMessage(input);

            // Create request from context
            var request = context.CreateRequest();

            try
            {
                Console.Write("Assistant: ");
                
                // Stream the response
                var fullResponse = "";
                await foreach (var chunk in llmClient.StreamCompleteAsync(request))
                {
                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        Console.Write(chunk.TextDelta);
                        fullResponse += chunk.TextDelta;
                    }
                }
                Console.WriteLine("\n");

                // Add assistant response to context
                context.AddAssistantMessage(fullResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\n");
            }
        }

        Console.WriteLine("Goodbye!");
    }
}

/* 
Required Environment Variables:
==============================
For OpenAI:
- OPENAI_API_KEY: Your OpenAI API key
- OPENAI_MODEL (optional): Model to use (defaults to gpt-4o-mini)
- OPENAI_API_BASE (optional): Custom API endpoint

For Cerebras:
- CEREBRAS_API_KEY: Your Cerebras API key
- CEREBRAS_MODEL (optional): Model to use (defaults to llama3.1-70b)

For Azure OpenAI:
- AZURE_OPENAI_ENDPOINT: Your Azure OpenAI endpoint
- AZURE_OPENAI_KEY: Your Azure OpenAI key
- AZURE_OPENAI_DEPLOYMENT: Your deployment name
- AZURE_OPENAI_API_VERSION (optional): API version (defaults to 2024-02-15-preview)

For Local/Ollama:
- OLLAMA_API_BASE: Your local Ollama endpoint (e.g., http://localhost:11434)
- OLLAMA_MODEL: Model to use (e.g., llama2)
*/