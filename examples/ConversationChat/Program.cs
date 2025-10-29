using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example: Multi-turn conversation with context management

// Load configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddCleanConsole());
services.AddLlmServices(configuration);
services.ConfigureLlmFromEnvironment();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
    var llmProvider = await factory.CreateAvailableProviderAsync();

    // Create a conversation context
    var messages = new List<Message>
    {
        new Message { Role = Role.System, Content = "You are a helpful AI assistant. Keep your responses concise." }
    };
    const int maxContextMessages = 10; // Keep last 10 messages

    logger.LogInformation("=== Conversation Chat Example ===");
    logger.LogInformation("Type 'exit' to quit, 'clear' to reset conversation, 'summary' to see context\n");

    while (true)
    {
        Console.Write("You: ");
        var input = Console.ReadLine();

        if (string.IsNullOrEmpty(input))
        {
            continue;
        }

        if (input.ToLower() == "exit")
        {
            break;
        }

        if (input.ToLower() == "clear")
        {
            messages.Clear();
            messages.Add(new Message { Role = Role.System, Content = "You are a helpful AI assistant. Keep your responses concise." });
            logger.LogInformation("[Conversation cleared]\n");
            continue;
        }

        if (input.ToLower() == "summary")
        {
            logger.LogInformation("\n[Conversation Summary]");
            logger.LogInformation("Messages in context: {Count}", messages.Count);
            var charCount = messages.Sum(m => m.Content?.Length ?? 0);
            logger.LogInformation("Character count: {CharCount}\n", charCount);
            continue;
        }

        try
        {
            // Add user message
            messages.Add(new Message { Role = Role.User, Content = input });

            // Trim context if needed
            while (messages.Count > maxContextMessages + 1) // +1 for system message
            {
                messages.RemoveAt(1); // Keep system message at index 0
            }

            // Create request with conversation context
            var request = new LlmRequest
            {
                Messages = messages,
                Config = new LlmClientConfig { MaxTokens = 500 }
            };

            // Get response
            Console.Write("Assistant: ");
            var response = await llmProvider.CompleteAsync(request);
            Console.WriteLine(response.Content);

            // Add assistant response to context
            messages.Add(new Message { Role = Role.Assistant, Content = response.Content });

            // Show token usage if available
            if (response.Usage != null)
            {
                logger.LogInformation("[Tokens used: {TokensUsed}]", response.Usage.TotalTokens);
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            logger.LogError("Error processing request: {Message}", ex.Message);
            logger.LogDebug(ex, "Full error details");
        }
    }

    logger.LogInformation("Goodbye!");
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during the conversation");
}

// Simple Program class for logger
partial class Program { }
