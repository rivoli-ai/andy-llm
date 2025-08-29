using Andy.Llm;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Example: Simple text completion with OpenAI and Cerebras

// Setup services
var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddCleanConsole());

// Configure from environment variables
// Set OPENAI_API_KEY and/or CEREBRAS_API_KEY
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
});

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    // Example 1: Using LlmClient directly (OpenAI by default)
    logger.LogInformation("=== Example 1: Simple OpenAI Completion ===");
    var client = serviceProvider.GetRequiredService<LlmClient>();
    var response = await client.GetResponseAsync("What is the capital of France?", "gpt-4o-mini");
    logger.LogInformation("Response: {Response}\n", response);

    // Example 2: Using Cerebras provider explicitly
    logger.LogInformation("=== Example 2: Cerebras Completion ===");
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
        logger.LogInformation("Cerebras Response: {Response}\n", cerebrasResponse.Content);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning("Cerebras not configured: {Message}", ex.Message);
        logger.LogWarning("Set CEREBRAS_API_KEY environment variable to use Cerebras\n");
    }

    // Example 3: Streaming response
    logger.LogInformation("=== Example 3: Streaming Response ===");
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

    logger.LogInformation("Streaming: ");
    await foreach (var chunk in client.StreamCompleteAsync(streamRequest))
    {
        if (!string.IsNullOrEmpty(chunk.TextDelta))
        {
            // For streaming, we still need to write directly to console for real-time output
            Console.Write(chunk.TextDelta);
        }
    }
    logger.LogInformation("\n");

    logger.LogInformation("Examples completed!");
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during the examples");
}

// Simple Program class for logger
partial class Program { }
