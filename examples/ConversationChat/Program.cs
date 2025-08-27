using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example: Multi-turn conversation with context management

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
});

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<LlmClient>();

// Create a conversation context
var conversation = new ConversationContext
{
    SystemInstruction = "You are a helpful AI assistant. Keep your responses concise.",
    MaxContextMessages = 10 // Keep last 10 messages
};

Console.WriteLine("=== Conversation Chat Example ===");
Console.WriteLine("Type 'exit' to quit, 'clear' to reset conversation, 'summary' to see context\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    
    if (string.IsNullOrEmpty(input))
        continue;
        
    if (input.ToLower() == "exit")
        break;
        
    if (input.ToLower() == "clear")
    {
        conversation.Clear();
        conversation.SystemInstruction = "You are a helpful AI assistant. Keep your responses concise.";
        Console.WriteLine("[Conversation cleared]\n");
        continue;
    }
    
    if (input.ToLower() == "summary")
    {
        Console.WriteLine("\n[Conversation Summary]");
        Console.WriteLine(conversation.GetSummary());
        Console.WriteLine($"Character count: {conversation.GetCharacterCount()}\n");
        continue;
    }
    
    // Add user message
    conversation.AddUserMessage(input);
    
    // Create request with conversation context
    var request = conversation.CreateRequest("gpt-4o-mini");
    
    // Get response
    Console.Write("Assistant: ");
    var response = await client.CompleteAsync(request);
    Console.WriteLine(response.Content);
    
    // Add assistant response to context
    conversation.AddAssistantMessage(response.Content);
    
    // Show token usage if available
    if (response.TokensUsed.HasValue)
    {
        Console.WriteLine($"[Tokens used: {response.TokensUsed}]");
    }
    
    Console.WriteLine();
}

Console.WriteLine("Goodbye!");