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
    private readonly Dictionary<string, ILlmProvider> _providerCache = new();

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

        // Check cache first
        if (_providerCache.TryGetValue(providerName, out var cached))
        {
            return cached;
        }

        // Get the configuration for this provider name
        if (!_options.Value.Providers.TryGetValue(providerName, out var config))
        {
            throw new NotSupportedException($"Provider configuration '{providerName}' not found");
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
            providerType = providerName.Contains('/')
                ? providerName.Split('/')[0]
                : providerName;
        }

        _logger.LogInformation("Creating LLM provider - Configuration: {ConfigName}, Provider: {ProviderType}, Model: {Model}",
            providerName, providerType.ToUpper(), config.Model ?? config.DeploymentName ?? "default");

        ILlmProvider provider;

        switch (providerType)
        {
            case "openai":
                provider = _serviceProvider.GetRequiredService<Providers.OpenAIProvider>();
                break;
            case "cerebras":
                provider = _serviceProvider.GetRequiredService<Providers.CerebrasProvider>();
                break;
            case "azure" or "azure-openai":
                provider = _serviceProvider.GetRequiredService<Providers.AzureOpenAIProvider>();
                break;
            case "local" or "ollama":
                provider = _serviceProvider.GetRequiredService<Providers.OllamaProvider>();
                break;
            default:
                throw new NotSupportedException($"Provider type '{providerType}' is not supported");
        }

        _providerCache[providerName] = provider;
        return provider;
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
