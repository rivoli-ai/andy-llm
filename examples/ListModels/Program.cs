using Andy.Llm;
using Andy.Llm.Llm;
using Andy.Llm.Providers;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Andy.Llm.Examples.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating how to list available models from different providers
/// </summary>
public class ListModels
{
    public static async Task Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddLogging(builder => builder.AddCleanConsole());

        // Configure providers
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            
            // OpenAI configuration
            options.Providers["openai"] = new ProviderConfig
            {
                ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                Model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o",
                Enabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
            };
            
            // Cerebras configuration
            options.Providers["cerebras"] = new ProviderConfig
            {
                ApiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY"),
                Model = Environment.GetEnvironmentVariable("CEREBRAS_MODEL") ?? "llama-3.3-70b",
                Enabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CEREBRAS_API_KEY"))
            };
            
            // Azure OpenAI configuration
            options.Providers["azure"] = new ProviderConfig
            {
                ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"),
                ApiBase = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT"),
                DeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT"),
                Enabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"))
            };
            
            // Ollama configuration
            options.Providers["ollama"] = new ProviderConfig
            {
                ApiBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434",
                Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "gpt-oss:20b",
                Enabled = true
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<ListModels>>();
        
        try
        {
            var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

            logger.LogInformation("=== List Models Example ===\n");
            logger.LogInformation("This example demonstrates listing available models from different LLM providers.\n");

            // List models from each provider
            await ListProviderModels(factory, "openai", logger);
            await ListProviderModels(factory, "cerebras", logger);
            await ListProviderModels(factory, "azure", logger);
            await ListProviderModels(factory, "ollama", logger);

            // Example: List all available models from all providers
            await ListAllModels(factory, logger);

            logger.LogInformation("\nModel listing completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during model listing");
        }
    }

    static async Task ListProviderModels(ILlmProviderFactory factory, string providerName, ILogger logger)
    {
        try
        {
            logger.LogInformation("\n=== {Provider} Models ===", providerName.ToUpper());
            
            var provider = factory.CreateProvider(providerName);
            
            if (!await provider.IsAvailableAsync())
            {
                logger.LogWarning("{Provider} is not available. Skipping...", providerName);
                return;
            }

            var models = await provider.ListModelsAsync();
            
            if (!models.Any())
            {
                logger.LogInformation("No models found for {Provider}", providerName);
                return;
            }

            foreach (var model in models)
            {
                logger.LogInformation("\nModel: {ModelId}", model.Id);
                
                if (!string.IsNullOrEmpty(model.Description))
                    logger.LogInformation("  Description: {Description}", model.Description);
                
                if (!string.IsNullOrEmpty(model.Family))
                    logger.LogInformation("  Family: {Family}", model.Family);
                
                if (!string.IsNullOrEmpty(model.ParameterSize))
                    logger.LogInformation("  Size: {Size}", model.ParameterSize);
                
                if (model.MaxTokens.HasValue)
                    logger.LogInformation("  Max Tokens: {MaxTokens:N0}", model.MaxTokens);
                
                if (model.SupportsFunctions.HasValue)
                    logger.LogInformation("  Function Calling: {Supported}", 
                        model.SupportsFunctions.Value ? "Yes" : "No");
                
                if (model.SupportsVision.HasValue)
                    logger.LogInformation("  Vision Support: {Supported}", 
                        model.SupportsVision.Value ? "Yes" : "No");
                
                if (model.IsFineTuned.HasValue && model.IsFineTuned.Value)
                    logger.LogInformation("  Type: Fine-tuned");
                
                if (model.Created.HasValue)
                    logger.LogInformation("  Created: {Created:yyyy-MM-dd}", model.Created);
                
                if (model.Metadata != null && model.Metadata.Any())
                {
                    logger.LogInformation("  Metadata:");
                    foreach (var kvp in model.Metadata)
                    {
                        logger.LogInformation("    {Key}: {Value}", kvp.Key, kvp.Value);
                    }
                }
            }
            
            logger.LogInformation("\nTotal {Provider} models: {Count}", providerName, models.Count());
        }
        catch (Exception ex)
        {
            logger.LogError("Failed to list models for {Provider}: {Message}", 
                providerName, ex.Message);
        }
    }

    static async Task ListAllModels(ILlmProviderFactory factory, ILogger logger)
    {
        logger.LogInformation("\n\n=== SUMMARY: All Available Models ===");
        
        var providers = new[] { "openai", "cerebras", "azure", "ollama" };
        var allModels = new List<(string Provider, string ModelId, string? Description)>();
        
        foreach (var providerName in providers)
        {
            try
            {
                var provider = factory.CreateProvider(providerName);
                
                if (!await provider.IsAvailableAsync())
                    continue;
                
                var models = await provider.ListModelsAsync();
                
                foreach (var model in models)
                {
                    allModels.Add((providerName, model.Id, model.Description));
                }
            }
            catch
            {
                // Skip failed providers
            }
        }
        
        if (allModels.Any())
        {
            logger.LogInformation("\nAll available models ({Count} total):", allModels.Count);
            
            // Group by provider
            var groupedModels = allModels.GroupBy(m => m.Provider);
            
            foreach (var group in groupedModels)
            {
                logger.LogInformation("\n{Provider}:", group.Key.ToUpper());
                foreach (var model in group)
                {
                    if (!string.IsNullOrEmpty(model.Description))
                        logger.LogInformation("  - {ModelId}: {Description}", model.ModelId, model.Description);
                    else
                        logger.LogInformation("  - {ModelId}", model.ModelId);
                }
            }
        }
        else
        {
            logger.LogWarning("No models available from any provider");
        }
    }
}