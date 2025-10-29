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
    /// Adds LLM services to the service collection.
    /// This method MERGES configuration from IConfiguration with existing configuration.
    /// Existing values take precedence over new values from IConfiguration.
    /// </summary>
    public static IServiceCollection AddLlmServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration with merging logic
        services.Configure<LlmOptions>(options =>
        {
            var configSection = configuration.GetSection("Llm");
            if (!configSection.Exists())
            {
                return;
            }

            // Bind to a temporary object to get values from IConfiguration
            var tempOptions = new LlmOptions();
            configSection.Bind(tempOptions);

            // Merge DefaultProvider (only if not already set)
            if (string.IsNullOrEmpty(options.DefaultProvider))
            {
                options.DefaultProvider = tempOptions.DefaultProvider;
            }

            // Merge Providers
            foreach (var (providerName, providerConfig) in tempOptions.Providers)
            {
                // Find existing provider case-insensitively
                var existingKey = options.Providers.Keys.FirstOrDefault(k =>
                    k.Equals(providerName, StringComparison.OrdinalIgnoreCase));

                if (existingKey != null && options.Providers.TryGetValue(existingKey, out var existing))
                {
                    // Merge strategy:
                    // - ApiKey: prefer existing (from environment variables)
                    // - Model: prefer configuration (appsettings.json overrides environment)
                    // - Other fields: prefer existing if set, otherwise use configuration

                    existing.ApiKey ??= providerConfig.ApiKey;
                    existing.ApiBase ??= providerConfig.ApiBase;

                    // Model from configuration ALWAYS overrides environment variable
                    if (!string.IsNullOrEmpty(providerConfig.Model))
                    {
                        existing.Model = providerConfig.Model;
                    }

                    existing.Organization ??= providerConfig.Organization;
                    existing.ApiVersion ??= providerConfig.ApiVersion;
                    existing.DeploymentName ??= providerConfig.DeploymentName;

                    // Enabled and Priority from configuration take precedence
                    if (providerConfig.Enabled != existing.Enabled)
                    {
                        existing.Enabled = providerConfig.Enabled;
                    }
                    if (providerConfig.Priority.HasValue)
                    {
                        existing.Priority = providerConfig.Priority;
                    }
                }
                else
                {
                    // No existing config, add from IConfiguration
                    options.Providers[providerName] = providerConfig;
                }
            }

            // Merge other settings (only if not already set)
            options.DefaultModel ??= tempOptions.DefaultModel;
            if (options.DefaultTemperature == 0.7)  // Default value
            {
                options.DefaultTemperature = tempOptions.DefaultTemperature;
            }
            if (options.DefaultMaxTokens == 4096)  // Default value
            {
                options.DefaultMaxTokens = tempOptions.DefaultMaxTokens;
            }
        });

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
    /// Configures LLM options from environment variables.
    /// This method MERGES environment variable configuration with existing configuration from appsettings.json.
    /// Existing values in configuration take precedence (e.g., Model from appsettings.json won't be overridden).
    /// </summary>
    public static IServiceCollection ConfigureLlmFromEnvironment(
        this IServiceCollection services)
    {
        services.Configure<LlmOptions>(options =>
        {
            // Detect if we're in "legacy mode" (no appsettings.json configuration)
            // In legacy mode, environment variables can create providers for backward compatibility
            // In modern mode (with appsettings.json), environment variables only merge into existing configs
            var hasExistingConfig = options.Providers.Any();

            // Helper to check if a value is a placeholder like "${OPENAI_API_KEY}"
            static bool IsPlaceholder(string? value) =>
                !string.IsNullOrEmpty(value) && value.StartsWith("${") && value.EndsWith("}");

            // Helper method to merge provider configuration from environment variables
            void MergeProviderConfig(string providerType, ProviderConfig envConfig)
            {
                // Find all existing providers of this type (e.g., "openai/latest-small" has Provider="openai")
                // This allows hierarchical configurations to receive environment variable overrides
                var matchingProviders = options.Providers
                    .Where(p => string.Equals(p.Value.Provider ?? p.Key, providerType, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matchingProviders.Any())
                {
                    // Merge into ALL matching providers (e.g., all openai/* configs)
                    foreach (var kvp in matchingProviders)
                    {
                        var existing = kvp.Value;
                        // Merge: override null, empty, or placeholder values (like "${OPENAI_API_KEY}") from environment
                        if (string.IsNullOrEmpty(existing.ApiKey) || IsPlaceholder(existing.ApiKey))
                            existing.ApiKey = envConfig.ApiKey;
                        if (string.IsNullOrEmpty(existing.ApiBase) || IsPlaceholder(existing.ApiBase))
                            existing.ApiBase = envConfig.ApiBase;
                        if (string.IsNullOrEmpty(existing.Model) || IsPlaceholder(existing.Model))
                            existing.Model = envConfig.Model;
                        if (string.IsNullOrEmpty(existing.Organization) || IsPlaceholder(existing.Organization))
                            existing.Organization = envConfig.Organization;
                        if (string.IsNullOrEmpty(existing.ApiVersion) || IsPlaceholder(existing.ApiVersion))
                            existing.ApiVersion = envConfig.ApiVersion;
                        if (string.IsNullOrEmpty(existing.DeploymentName) || IsPlaceholder(existing.DeploymentName))
                            existing.DeploymentName = envConfig.DeploymentName;
                        // CRITICAL: Keep existing Enabled and Priority values - NEVER override from environment!
                    }
                }
                else if (!hasExistingConfig)
                {
                    // Legacy mode: No appsettings.json configuration exists at all
                    // Create provider from environment variables for backward compatibility
                    options.Providers[providerType] = envConfig;
                }
                // If we have existing config but no matching provider, DO NOTHING.
                // This means the user explicitly configured providers and chose not to include this one.
            }

            // OpenAI configuration
            var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var openAiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE");
            var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
            var openAiOrg = Environment.GetEnvironmentVariable("OPENAI_ORGANIZATION");

            if (!string.IsNullOrEmpty(openAiKey))
            {
                MergeProviderConfig("openai", new ProviderConfig
                {
                    ApiKey = openAiKey,
                    ApiBase = openAiBase,
                    Model = openAiModel,
                    Organization = openAiOrg,
                    Enabled = true
                });
            }

            // Azure OpenAI configuration
            var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var azureKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var azureDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
            var azureVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION");

            if (!string.IsNullOrEmpty(azureEndpoint) && !string.IsNullOrEmpty(azureKey))
            {
                MergeProviderConfig("azure", new ProviderConfig
                {
                    ApiKey = azureKey,
                    ApiBase = azureEndpoint,
                    DeploymentName = azureDeployment,
                    ApiVersion = azureVersion ?? "2024-02-15-preview",
                    Enabled = true
                });
            }

            // Cerebras configuration
            var cerebrasKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
            var cerebrasBase = Environment.GetEnvironmentVariable("CEREBRAS_API_BASE");
            var cerebrasModel = Environment.GetEnvironmentVariable("CEREBRAS_MODEL");

            if (!string.IsNullOrEmpty(cerebrasKey))
            {
                MergeProviderConfig("cerebras", new ProviderConfig
                {
                    ApiKey = cerebrasKey,
                    ApiBase = cerebrasBase,
                    Model = cerebrasModel,
                    Enabled = true
                });
            }

            // Local/Ollama configuration
            var ollamaBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE");
            var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");

            if (!string.IsNullOrEmpty(ollamaBase))
            {
                MergeProviderConfig("ollama", new ProviderConfig
                {
                    ApiBase = ollamaBase,
                    Model = ollamaModel,
                    Enabled = true
                });
            }

            // Only set default provider if not already configured
            if (string.IsNullOrEmpty(options.DefaultProvider) && options.Providers.Any(p => p.Value.Enabled))
            {
                options.DefaultProvider = options.Providers.First(p => p.Value.Enabled).Key;
            }
        });

        return services;
    }
}
