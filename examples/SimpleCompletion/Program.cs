using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class SimpleCompletion
{
    public static async Task Main()
    {
// Example: Simple text completion with OpenAI and Cerebras
// Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

// Setup services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddCleanConsole());
// Configure providers from appsettings.json, then environment variables will merge
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<SimpleCompletion>>();
        try
        {
            // Example 1: Using default provider (openai/latest-small from appsettings.json)
            logger.LogInformation("=== Example 1: Simple OpenAI Completion ===");
            var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
            var provider = await factory.CreateAvailableProviderAsync();

            var userPrompt1 = "What is the capital of France?";
            logger.LogInformation("User: {Prompt}", userPrompt1);

            var request = new LlmRequest
            {
                Messages = new List<Message>
                {
                    new Message {Role = Role.User, Content = userPrompt1}
                }
            };

            var response = await provider.CompleteAsync(request);
            logger.LogInformation("Assistant: {Response}\n", response.Content);
            // Example 2: Using Cerebras provider explicitly
            logger.LogInformation("=== Example 2: Cerebras Completion ===");
            try
            {
                var cerebrasProvider = factory.CreateProvider("cerebras/fast-large");

                var userPrompt2 = "Explain quantum computing in one sentence.";
                logger.LogInformation("User: {Prompt}", userPrompt2);

                var cerebrasRequest = new LlmRequest
                {
                    Messages = new List<Message>
                    {
                        new Message {Role = Role.User, Content = userPrompt2}
                    },
                    // Model is optional - will use default llama3.1-8b
                    Config = new LlmClientConfig {MaxTokens = 100, Temperature = 0.7M}
                };
                var cerebrasResponse = await cerebrasProvider.CompleteAsync(cerebrasRequest);
                logger.LogInformation("Assistant: {Response}\n", cerebrasResponse.Content);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Cerebras not configured: {Message}", ex.Message);
                logger.LogWarning("Set CEREBRAS_API_KEY environment variable to use Cerebras\n");
            }

            // Example 3: Streaming response
            logger.LogInformation("=== Example 3: Streaming Response ===");

            var userPrompt3 = "Count from 1 to 5 slowly, with a brief description for each number.";
            logger.LogInformation("User: {Prompt}", userPrompt3);
            logger.LogInformation("Assistant (streaming): ");

            var streamRequest = new LlmRequest
            {
                Messages = new List<Message> {new Message {Role = Role.User, Content = userPrompt3}},
                Config = new LlmClientConfig {Model = "gpt-4o-mini", MaxTokens = 200}
            };

            var streamedText = string.Empty;
            await foreach (var chunk in provider.StreamCompleteAsync(streamRequest))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    // For streaming, we still need to write directly to console for real-time output
                    Console.Write(chunk.TextDelta);
                    streamedText += chunk.TextDelta;
                }
            }

            // Add completion indicators
            if (!string.IsNullOrEmpty(streamedText))
            {
                Console.WriteLine("\n[Streaming completed]");
            }

            logger.LogInformation("");
            logger.LogInformation("Examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during the examples");
        }
    }
}
