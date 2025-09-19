using Andy.Configuration;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Llm.Services;
using Andy.Model.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Andy.Llm.Extensions;

/// <summary>
/// Extension methods for configuring LLM services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds LLM services to the service collection
    /// </summary>
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));

        // Register Andy.Configuration if needed
        services.AddAndyConfiguration(configuration);

        // Register providers
        services.AddSingleton<OpenAIProvider>();
        services.AddSingleton<CerebrasProvider>();
        services.AddSingleton<AzureOpenAIProvider>();
        services.AddSingleton<OllamaProvider>();

        // Register HttpClientFactory for providers that need it
        services.AddHttpClient();

        // Register factory
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

        return services;
    }

    /// <summary>
    /// Adds LLM services with custom configuration
    /// </summary>
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        Action<LlmOptions> configure)
    {
        services.Configure(configure);

        // Register providers
        services.AddSingleton<OpenAIProvider>();
        services.AddSingleton<CerebrasProvider>();
        services.AddSingleton<AzureOpenAIProvider>();
        services.AddSingleton<OllamaProvider>();

        // Register HttpClientFactory for providers that need it
        services.AddHttpClient();

        // Register factory
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

        return services;
    }

    /// <summary>
    /// Adds a custom LLM provider
    /// </summary>
    public static IServiceCollection AddLlmProvider<TProvider>(
        this IServiceCollection services)
        where TProvider : class, ILlmProvider
    {
        services.AddSingleton<TProvider>();
        return services;
    }

    /// <summary>
    /// Configures LLM options from environment variables
    /// </summary>
    public static IServiceCollection ConfigureLlmFromEnvironment(
        this IServiceCollection services)
    {
        services.Configure<LlmOptions>(options =>
        {
            // OpenAI configuration
            var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var openAiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE");
            var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            var openAiOrg = Environment.GetEnvironmentVariable("OPENAI_ORGANIZATION");

            if (!string.IsNullOrEmpty(openAiKey))
            {
                options.Providers["openai"] = new ProviderConfig
                {
                    ApiKey = openAiKey,
                    ApiBase = openAiBase,
                    Model = openAiModel ?? "gpt-4o",
                    Organization = openAiOrg,
                    Enabled = true
                };
            }

            // Azure OpenAI configuration
            var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
            var azureVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION");

            if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
            {
                options.Providers["azure"] = new ProviderConfig
                {
                    ApiKey = azureKey,
                    ApiBase = azureEndpoint,
                    DeploymentName = azureDeployment,
                    ApiVersion = azureVersion ?? "2024-02-15-preview",
                    Enabled = true
                };
            }

            // Cerebras configuration
            var cerebrasKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
            var cerebrasModel = Environment.GetEnvironmentVariable("CEREBRAS_MODEL");

            if (!string.IsNullOrEmpty(cerebrasKey))
            {
                options.Providers["cerebras"] = new ProviderConfig
                {
                    ApiKey = cerebrasKey,
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = cerebrasModel ?? "llama3.1-8b",
                    Enabled = true
                };
            }

            // Local/Ollama configuration
            var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE");
            var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");

            if (!string.IsNullOrEmpty(ollamaBase))
            {
                options.Providers["ollama"] = new ProviderConfig
                {
                    ApiBase = ollamaBase,
                    Model = ollamaModel ?? "llama2",
                    Enabled = true
                };
            }

            // Set default provider based on what's configured
            if (options.Providers.Any(p => p.Value.Enabled))
            {
                options.DefaultProvider = options.Providers.First(p => p.Value.Enabled).Key;
            }
        });

        return services;
    }
}
