using System.Collections.Concurrent;
using Andy.Llm.Configuration;
using Andy.Model.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Llm.Providers;

/// <summary>
/// Factory for creating LLM providers based on configuration
/// </summary>
public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<LlmProviderFactory> _logger;
    private readonly ConcurrentDictionary<string, ILlmProvider> _providerCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmProviderFactory"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="options">The LLM options.</param>
    /// <param name="logger">The logger.</param>
    public LlmProviderFactory(
        IServiceProvider serviceProvider,
        IOptions<LlmOptions> options,
        ILogger<LlmProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates an LLM provider instance.
    /// </summary>
    /// <param name="providerName">The configuration name (e.g., "openai/latest-large" or "openai"). If null, uses the default provider.</param>
    /// <returns>The LLM provider instance.</returns>
    public ILlmProvider CreateProvider(string? providerName = null)
    {
        providerName = (providerName ?? _options.Value.DefaultProvider).ToLowerInvariant();

        // Use GetOrAdd for thread-safe lazy initialization
        // This ensures only one thread creates the provider even under concurrent access
        return _providerCache.GetOrAdd(providerName, key =>
        {
            // Try to get the configuration for this provider name
            ProviderConfig? config;
            string configKey = key;

            // First try exact match
            if (!_options.Value.Providers.TryGetValue(key, out config))
            {
                // If no exact match, try to find by provider type
                // e.g., "cerebras" should find "cerebras/large-code" if Provider="cerebras"
                var matchingProvider = _options.Value.Providers
                    .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key.Split('/')[0], key, StringComparison.OrdinalIgnoreCase));

                if (matchingProvider.Value != null)
                {
                    config = matchingProvider.Value;
                    configKey = matchingProvider.Key;

                    // If we found a match with a different key, also cache it under the actual config key
                    // to avoid duplicate lookups later
                    if (!string.Equals(key, configKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // Will be added after creation below
                    }
                }
                else
                {
                    throw new NotSupportedException($"Provider configuration '{key}' not found");
                }
            }

            // Determine the actual provider type
            // If Provider property is set, use it; otherwise infer from the configuration name
            string providerType;
            if (!string.IsNullOrEmpty(config.Provider))
            {
                providerType = config.Provider.ToLowerInvariant();
            }
            else
            {
                // Infer from configuration name (e.g., "openai/latest-large" -> "openai")
                providerType = key.Contains('/')
                    ? key.Split('/')[0]
                    : key;
            }

            _logger.LogInformation("Creating LLM provider - Configuration: {ConfigName}, Provider: {ProviderType}, Model: {Model}",
                configKey, providerType.ToUpper(), config.Model ?? config.DeploymentName ?? "default");

            // Clone the config to avoid modifying the shared configuration object
            // This prevents race conditions when multiple threads access the same config
            var configCopy = new Configuration.ProviderConfig
            {
                Provider = config.Provider,
                ApiKey = config.ApiKey,
                ApiBase = config.ApiBase,
                Model = config.Model,
                Organization = config.Organization,
                DeploymentName = config.DeploymentName,
                ApiVersion = config.ApiVersion,
                Enabled = config.Enabled,
                Priority = config.Priority
            };

            // Apply environment variable fallbacks for missing values
            // This ensures backward compatibility with tests and configurations that rely on env vars
            ApplyEnvironmentFallbacks(configCopy, providerType);

            // Create provider instance with specific configuration
            ILlmProvider provider;
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();

            switch (providerType)
            {
                case "openai":
                    var openaiLogger = loggerFactory.CreateLogger<Providers.OpenAIProvider>();
                    provider = new Providers.OpenAIProvider(configCopy, configKey, openaiLogger, httpClientFactory);
                    break;
                case "cerebras":
                    var cerebrasLogger = loggerFactory.CreateLogger<Providers.CerebrasProvider>();
                    provider = new Providers.CerebrasProvider(configCopy, configKey, cerebrasLogger, httpClientFactory);
                    break;
                case "azure" or "azure-openai":
                    var azureLogger = loggerFactory.CreateLogger<Providers.AzureOpenAIProvider>();
                    provider = new Providers.AzureOpenAIProvider(configCopy, configKey, azureLogger);
                    break;
                case "local" or "ollama":
                    var ollamaLogger = loggerFactory.CreateLogger<Providers.OllamaProvider>();
                    provider = new Providers.OllamaProvider(configCopy, configKey, ollamaLogger, httpClientFactory);
                    break;
                default:
                    throw new NotSupportedException($"Provider type '{providerType}' is not supported");
            }

            // If the lookup key differs from the actual config key, also cache under the config key
            if (!string.Equals(key, configKey, StringComparison.OrdinalIgnoreCase))
            {
                _providerCache.TryAdd(configKey, provider);
            }

            return provider;
        });
    }

    /// <summary>
    /// Creates the first available LLM provider based on priority rules:
    /// 1. Default provider (if fully configured)
    /// 2. Providers with Priority set (highest to lowest)
    /// 3. Remaining providers without Priority (in dictionary order)
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The first available LLM provider.</returns>
    public async Task<ILlmProvider> CreateAvailableProviderAsync(CancellationToken cancellationToken = default)
    {
        // Helper to check if provider is fully configured
        bool IsFullyConfigured(ProviderConfig config, string configName)
        {
            // Determine the actual provider type
            string providerType;
            if (!string.IsNullOrEmpty(config.Provider))
            {
                providerType = config.Provider.ToLowerInvariant();
            }
            else
            {
                // Infer from configuration name (e.g., "openai/latest-large" -> "openai")
                providerType = configName.Contains('/')
                    ? configName.Split('/')[0].ToLowerInvariant()
                    : configName.ToLowerInvariant();
            }

            // For Ollama, ApiKey is not required
            if (providerType == "ollama" || providerType == "local")
            {
                return !string.IsNullOrEmpty(config.ApiBase) && !string.IsNullOrEmpty(config.Model);
            }

            // For Azure, DeploymentName is required instead of Model
            if (providerType == "azure" || providerType == "azure-openai")
            {
                return !string.IsNullOrEmpty(config.ApiKey) &&
                       !string.IsNullOrEmpty(config.ApiBase) &&
                       !string.IsNullOrEmpty(config.DeploymentName);
            }

            // For other providers, ApiKey, ApiBase, and Model are required
            return !string.IsNullOrEmpty(config.ApiKey) &&
                   !string.IsNullOrEmpty(config.ApiBase) &&
                   !string.IsNullOrEmpty(config.Model);
        }

        // Build ordered list of providers to try
        var providersToTry = new List<(string name, ProviderConfig config)>();

        // 1. Try the default provider first (if fully configured)
        var defaultProviderName = _options.Value.DefaultProvider.ToLowerInvariant();
        if (_options.Value.Providers.TryGetValue(defaultProviderName, out var defaultConfig) &&
            defaultConfig.Enabled &&
            IsFullyConfigured(defaultConfig, defaultProviderName))
        {
            providersToTry.Add((defaultProviderName, defaultConfig));
        }

        // 2. Get providers with Priority (excluding default if already added)
        // Only include fully configured providers
        var providersWithPriority = _options.Value.Providers
            .Where(p => p.Value.Enabled &&
                       p.Value.Priority.HasValue &&
                       !p.Key.Equals(defaultProviderName, StringComparison.OrdinalIgnoreCase) &&
                       IsFullyConfigured(p.Value, p.Key))
            .OrderByDescending(p => p.Value.Priority!.Value)  // Highest priority first
            .Select(p => (p.Key, p.Value));

        providersToTry.AddRange(providersWithPriority);

        // 3. Get providers without Priority (excluding default if already added)
        // Only include fully configured providers
        var providersWithoutPriority = _options.Value.Providers
            .Where(p => p.Value.Enabled &&
                       !p.Value.Priority.HasValue &&
                       !p.Key.Equals(defaultProviderName, StringComparison.OrdinalIgnoreCase) &&
                       IsFullyConfigured(p.Value, p.Key))
            .Select(p => (p.Key, p.Value));

        providersToTry.AddRange(providersWithoutPriority);

        // Try each provider in priority order
        foreach (var (name, config) in providersToTry)
        {
            try
            {
                var provider = CreateProvider(name);
                if (await provider.IsAvailableAsync(cancellationToken))
                {
                    if (name.Equals(defaultProviderName, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Using default provider: {Provider}", provider.Name);
                    }
                    else
                    {
                        _logger.LogInformation("Using provider: {Provider} (Priority: {Priority})",
                            provider.Name, config.Priority?.ToString() ?? "none");
                    }
                    return provider;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} not available", name);
            }
        }

        throw new InvalidOperationException("No LLM providers are available");
    }

    /// <summary>
    /// Applies environment variable fallbacks to a provider configuration.
    /// Only fills in values that are missing, null, or placeholders.
    /// </summary>
    private static void ApplyEnvironmentFallbacks(ProviderConfig config, string providerType)
    {
        // Helper to check if a value is a placeholder like "${OPENAI_API_KEY}"
        static bool IsPlaceholder(string? value) =>
            !string.IsNullOrEmpty(value) && value.StartsWith("${") && value.EndsWith("}");

        switch (providerType.ToLowerInvariant())
        {
            case "openai":
                if (string.IsNullOrEmpty(config.ApiKey) || IsPlaceholder(config.ApiKey))
                    config.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(config.ApiBase) || IsPlaceholder(config.ApiBase))
                    config.ApiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE");
                if (string.IsNullOrEmpty(config.Model) || IsPlaceholder(config.Model))
                    config.Model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
                if (string.IsNullOrEmpty(config.Organization) || IsPlaceholder(config.Organization))
                    config.Organization = Environment.GetEnvironmentVariable("OPENAI_ORGANIZATION");
                break;

            case "cerebras":
                if (string.IsNullOrEmpty(config.ApiKey) || IsPlaceholder(config.ApiKey))
                    config.ApiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
                if (string.IsNullOrEmpty(config.ApiBase) || IsPlaceholder(config.ApiBase))
                    config.ApiBase = Environment.GetEnvironmentVariable("CEREBRAS_API_BASE");
                if (string.IsNullOrEmpty(config.Model) || IsPlaceholder(config.Model))
                    config.Model = Environment.GetEnvironmentVariable("CEREBRAS_MODEL");
                break;

            case "azure":
            case "azure-openai":
                if (string.IsNullOrEmpty(config.ApiKey) || IsPlaceholder(config.ApiKey))
                    config.ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
                if (string.IsNullOrEmpty(config.ApiBase) || IsPlaceholder(config.ApiBase))
                    config.ApiBase = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                if (string.IsNullOrEmpty(config.DeploymentName) || IsPlaceholder(config.DeploymentName))
                    config.DeploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
                if (string.IsNullOrEmpty(config.ApiVersion) || IsPlaceholder(config.ApiVersion))
                    config.ApiVersion = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_VERSION");
                break;

            case "local":
            case "ollama":
                if (string.IsNullOrEmpty(config.ApiBase) || IsPlaceholder(config.ApiBase))
                    config.ApiBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434";
                if (string.IsNullOrEmpty(config.Model) || IsPlaceholder(config.Model))
                    config.Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
                break;
        }
    }
}

/// <summary>
/// Factory interface for creating LLM providers
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// Creates a specific provider by name
    /// </summary>
    ILlmProvider CreateProvider(string? providerName = null);

    /// <summary>
    /// Creates the first available provider based on configuration
    /// </summary>
    Task<ILlmProvider> CreateAvailableProviderAsync(CancellationToken cancellationToken = default);
}
