using Andy.Llm;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example: Simple text completion with OpenAI and Cerebras

// Setup services
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

// Configure from environment variables
// Set OPENAI_API_KEY and/or CEREBRAS_API_KEY
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
});

var serviceProvider = services.BuildServiceProvider();

// Example 1: Using LlmClient directly (OpenAI by default)
Console.WriteLine("=== Example 1: Simple OpenAI Completion ===");
var client = serviceProvider.GetRequiredService<LlmClient>();
var response = await client.GetResponseAsync("What is the capital of France?", "gpt-4o-mini");
Console.WriteLine($"Response: {response}\n");

// Example 2: Using Cerebras provider explicitly
Console.WriteLine("=== Example 2: Cerebras Completion ===");
var factory = serviceProvider.GetRequiredService<Andy.Llm.Services.ILlmProviderFactory>();
try
{
    var cerebrasProvider = factory.CreateProvider("cerebras");
    var request = new Andy.Llm.Models.LlmRequest
    {
        Messages = new List<Andy.Llm.Models.Message>
        {
            Andy.Llm.Models.Message.CreateText(Andy.Llm.Models.MessageRole.User, "Explain quantum computing in one sentence.")
        },
        // Model is optional - will use default llama3.1-8b
        MaxTokens = 100,
        Temperature = 0.7
    };
    
    var cerebrasResponse = await cerebrasProvider.CompleteAsync(request);
    Console.WriteLine($"Cerebras Response: {cerebrasResponse.Content}\n");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Cerebras not configured: {ex.Message}");
    Console.WriteLine("Set CEREBRAS_API_KEY environment variable to use Cerebras\n");
}

// Example 3: Streaming response
Console.WriteLine("=== Example 3: Streaming Response ===");
var streamRequest = new Andy.Llm.Models.LlmRequest
{
    Messages = new List<Andy.Llm.Models.Message>
    {
        Andy.Llm.Models.Message.CreateText(Andy.Llm.Models.MessageRole.User, "Count from 1 to 5 slowly.")
    },
    Model = "gpt-4o-mini",
    MaxTokens = 100,
    Stream = true
};

Console.Write("Streaming: ");
await foreach (var chunk in client.StreamCompleteAsync(streamRequest))
{
    if (!string.IsNullOrEmpty(chunk.TextDelta))
    {
        Console.Write(chunk.TextDelta);
    }
}
Console.WriteLine("\n");

Console.WriteLine("Examples completed!");