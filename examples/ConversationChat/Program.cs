using Andy.Llm;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Models;
using Andy.Llm.Extensions;
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

    var client = serviceProvider.GetRequiredService<LlmClient>();

    // Create a conversation context
    var conversation = new ConversationContext
    {
        SystemInstruction = "You are a helpful AI assistant. Keep your responses concise.",
        MaxContextMessages = 10 // Keep last 10 messages
    };

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
            conversation.Clear();
            conversation.SystemInstruction = "You are a helpful AI assistant. Keep your responses concise.";
            logger.LogInformation("[Conversation cleared]\n");
            continue;
        }

        if (input.ToLower() == "summary")
        {
            logger.LogInformation("\n[Conversation Summary]");
            logger.LogInformation("{Summary}", conversation.GetSummary());
            logger.LogInformation("Character count: {CharCount}\n", conversation.GetCharacterCount());
            continue;
        }

        try
        {
            // Add user message
            conversation.AddUserMessage(input);

            // Create request with conversation context
            var request = conversation.CreateRequest(model);

            // Get response
            Console.Write("Assistant: ");
            var response = await client.CompleteAsync(request);
            Console.WriteLine(response.Content);

            // Add assistant response to context
            conversation.AddAssistantMessage(response.Content);

            // Show token usage if available
            if (response.TokensUsed.HasValue)
            {
                logger.LogInformation("[Tokens used: {TokensUsed}]", response.TokensUsed);
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
