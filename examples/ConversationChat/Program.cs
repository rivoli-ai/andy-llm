using Andy.Llm;
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

Console.WriteLine($"Using provider: {provider}, model: {model}");

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = provider;
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
Console.WriteLine($"Provider: {provider.ToUpper()}, Model: {model}");
Console.WriteLine("Type 'exit' to quit, 'clear' to reset conversation, 'summary' to see context");
Console.WriteLine("Set LLM_PROVIDER=cerebras or LLM_PROVIDER=openai to switch providers\n");

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
        Console.WriteLine($"[Tokens used: {response.TokensUsed}]");
    }
    
    Console.WriteLine();
}

Console.WriteLine("Goodbye!");