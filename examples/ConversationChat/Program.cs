using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example: Multi-turn conversation with context management

// Determine which provider to use
var providerEnv = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "openai";
var provider = providerEnv.ToLower();
// Use the correct model for each provider
var model = provider == "cerebras" ? "llama-3.3-70b" : "gpt-4o-mini";

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddCleanConsole());
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = provider;
});

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Using provider: {Provider}, model: {Model}", provider, model);

    var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
    var llmProvider = await factory.CreateAvailableProviderAsync();

    // Create a conversation context
    var messages = new List<Message>
    {
        new Message { Role = Role.System, Content = "You are a helpful AI assistant. Keep your responses concise." }
    };
    const int maxContextMessages = 10; // Keep last 10 messages

    logger.LogInformation("=== Conversation Chat Example ===");
    logger.LogInformation("Provider: {Provider}, Model: {Model}", provider.ToUpper(), model);
    logger.LogInformation("Type 'exit' to quit, 'clear' to reset conversation, 'summary' to see context");
    logger.LogInformation("Set LLM_PROVIDER=cerebras or LLM_PROVIDER=openai to switch providers\n");

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
                Config = new LlmClientConfig { Model = model, MaxTokens = 500 }
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
