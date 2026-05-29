using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Extensions;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating the OpenRouter provider.
///
/// OpenRouter (https://openrouter.ai) is a unified, OpenAI-compatible gateway
/// that fronts hundreds of models behind a single API key. This example uses a
/// FREE model (openai/gpt-oss-20b:free) by default, so it costs nothing to run.
///
/// Setup:
///   export OPENROUTER_API_KEY="sk-or-..."   # from https://openrouter.ai/keys
///   dotnet run --project examples/OpenRouter
///
/// Override the model with OPENROUTER_MODEL or by editing appsettings.json.
/// Model ids use the "provider/model" form, e.g. "anthropic/claude-sonnet-4.6".
/// </summary>
public class OpenRouterExample
{
    public static async Task Main()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddCleanConsole());

        // Bind providers from appsettings.json, then let OPENROUTER_* env vars merge in.
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<OpenRouterExample>>();

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")))
        {
            logger.LogError("OPENROUTER_API_KEY is not set. Get a key at https://openrouter.ai/keys and export it.");
            return;
        }

        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        try
        {
            logger.LogInformation("=== OpenRouter Example (free model) ===\n");

            await SimpleCompletion(factory, logger);
            await StreamingCompletion(factory, logger);
            await ToolCalling(factory, logger);
            await ListModels(factory, logger);

            logger.LogInformation("\nOpenRouter example completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenRouter example failed");
        }
    }

    static async Task SimpleCompletion(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== 1. Simple Completion ===");
        var provider = factory.CreateProvider("openrouter/free");

        var response = await CompleteWithRetryAsync(provider, new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "Say hello in exactly three words." }
            },
            Config = new LlmClientConfig { MaxTokens = 32 }
        }, logger);
        if (response is null)
        {
            return;
        }

        logger.LogInformation("  Model: {Model}", response.Model);
        logger.LogInformation("  Response: {Response}", response.Content);
        if (response.Usage != null)
        {
            logger.LogInformation("  Tokens: {Total}", response.Usage.TotalTokens);
        }
    }

    static async Task StreamingCompletion(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== 2. Streaming ===");
        var provider = factory.CreateProvider("openrouter/free");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "Name three primary colors." }
            },
            Config = new LlmClientConfig { MaxTokens = 64 }
        };

        // Retry the whole stream if the free model is rate-limited before any
        // content arrives (OpenRouter surfaces that as a terminal Error frame).
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var wroteAny = false;
            string? error = null;
            Console.Write("  ");
            await foreach (var chunk in provider.StreamCompleteAsync(request))
            {
                if (chunk.Delta?.Content is { Length: > 0 } text)
                {
                    Console.Write(text);
                    wroteAny = true;
                }
                if (chunk.IsComplete && !string.IsNullOrEmpty(chunk.Error))
                {
                    error = chunk.Error;
                }
            }
            Console.WriteLine();

            if (wroteAny || !IsRateLimited(error) || attempt == MaxAttempts)
            {
                break;
            }

            logger.LogWarning("  Rate-limited; retrying stream ({Attempt}/{Max})…", attempt, MaxAttempts);
            await Task.Delay(RetryDelay);
        }
    }

    static async Task ToolCalling(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== 3. Tool Calling ===");
        var provider = factory.CreateProvider("openrouter/free");

        var response = await CompleteWithRetryAsync(provider, new LlmRequest
        {
            Messages = new List<Message>
            {
                new() { Role = Role.User, Content = "What is 15% of 240? Use the calculate tool." }
            },
            Tools = new List<ToolDeclaration>
            {
                new()
                {
                    Name = "calculate",
                    Description = "Perform a mathematical calculation",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["expression"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The expression to evaluate, e.g. '0.15 * 240'"
                            }
                        },
                        ["required"] = new[] { "expression" }
                    }
                }
            },
            Config = new LlmClientConfig { MaxTokens = 256 }
        }, logger);
        if (response is null)
        {
            return;
        }

        if (response.HasToolCalls)
        {
            var call = response.ToolCalls[0];
            logger.LogInformation("  Tool requested: {Name}({Args})", call.Name, call.ArgumentsJson);
        }
        else
        {
            logger.LogInformation("  Response (no tool call): {Response}", response.Content);
        }
    }

    // OpenRouter's free model pool is shared and frequently rate-limited (HTTP
    // 429) with a short Retry-After. A couple of retries makes the demo reliable
    // without a paid model. Production code should use a real resilience policy.
    private const int MaxAttempts = 4;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private static bool IsRateLimited(string? message) =>
        message != null && message.Contains("429");

    static async Task<LlmResponse?> CompleteWithRetryAsync(
        ILlmProvider provider, LlmRequest request, ILogger logger)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await provider.CompleteAsync(request);
            }
            catch (InvalidOperationException ex) when (IsRateLimited(ex.Message) && attempt < MaxAttempts)
            {
                logger.LogWarning("  Rate-limited; retrying ({Attempt}/{Max})…", attempt, MaxAttempts);
                await Task.Delay(RetryDelay);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("  Request failed: {Message}", ex.Message);
                return null;
            }
        }
    }

    static async Task ListModels(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n=== 4. List Models (first 10) ===");
        var provider = factory.CreateProvider("openrouter/free");

        var models = (await provider.ListModelsAsync()).Take(10).ToList();
        foreach (var model in models)
        {
            logger.LogInformation("  {Id} (provider: {Provider})", model.Id, model.Provider);
        }
        logger.LogInformation("  (OpenRouter exposes hundreds more — see https://openrouter.ai/models)");
    }
}
