using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Andy.Llm.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class SimpleCompletion
{
    public static async Task Main()
    {
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
        var logger = serviceProvider.GetRequiredService<ILogger<SimpleCompletion>>();
        try
        {
            // Example 1: Using default provider (OpenAI by default)
            logger.LogInformation("=== Example 1: Simple OpenAI Completion ===");
            var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
            var provider = await factory.CreateAvailableProviderAsync();
            var request = new LlmRequest
            {
                Messages = new List<Message>
                {
                    new Message {Role = Role.User, Content = "What is the capital of France?"}
                },
                Config = new LlmClientConfig {Model = "gpt-4o-mini"}
            };

            var response = await provider.CompleteAsync(request);
            logger.LogInformation("Response: {Response}\n", response.Content);
            // Example 2: Using Cerebras provider explicitly
            logger.LogInformation("=== Example 2: Cerebras Completion ===");
            try
            {
                var cerebrasProvider = factory.CreateProvider("cerebras");
                var cerebrasRequest = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message {Role = Role.User, Content = "Explain quantum computing in one sentence."}
                    },
                    // Model is optional - will use default llama3.1-8b
                    Config = new LlmClientConfig {MaxTokens = 100, Temperature = 0.7M}
                };
                var cerebrasResponse = await cerebrasProvider.CompleteAsync(cerebrasRequest);
                logger.LogInformation("Cerebras Response: {Response}\n", cerebrasResponse.Content);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Cerebras not configured: {Message}", ex.Message);
                logger.LogWarning("Set CEREBRAS_API_KEY environment variable to use Cerebras\n");
            }

            // Example 3: Streaming response
            logger.LogInformation("=== Example 3: Streaming Response ===");
            var streamRequest = new LlmRequest
            {
                Messages =
                    new List<Message> {new Message {Role = Role.User, Content = "Count from 1 to 5 slowly."}},
                Config = new LlmClientConfig {Model = "gpt-4o-mini", MaxTokens = 100}
            };
            logger.LogInformation("Streaming: ");
            await foreach (var chunk in provider.StreamCompleteAsync(streamRequest))
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                    // For streaming, we still need to write directly to console for real-time output
                    Console.Write(chunk.TextDelta);
            logger.LogInformation("\n");
            logger.LogInformation("Examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the examples");
        }
    }
}
