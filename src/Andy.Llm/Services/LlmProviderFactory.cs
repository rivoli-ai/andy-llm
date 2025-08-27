using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Llm.Services;

/// <summary>
/// Factory for creating LLM providers based on configuration
/// </summary>
public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<LlmOptions> _options;
    private readonly ILogger<LlmProviderFactory> _logger;
    private readonly Dictionary<string, ILlmProvider> _providerCache = new();

    public LlmProviderFactory(
        IServiceProvider serviceProvider,
        IOptions<LlmOptions> options,
        ILogger<LlmProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ILlmProvider CreateProvider(string? providerName = null)
    {
        providerName = (providerName ?? _options.Value.DefaultProvider).ToLowerInvariant();

        // Check cache first
        if (_providerCache.TryGetValue(providerName, out var cached))
        {
            return cached;
        }

        _logger.LogDebug("Creating LLM provider: {Provider}", providerName);

        var provider = providerName switch
        {
            "openai" => _serviceProvider.GetRequiredService<Providers.OpenAIProvider>(),
            "cerebras" => _serviceProvider.GetRequiredService<Providers.CerebrasProvider>(),
            "azure" or "azure-openai" => CreateAzureProvider(),
            "local" or "ollama" => CreateLocalProvider(),
            _ => throw new NotSupportedException($"Provider '{providerName}' is not supported")
        };

        _providerCache[providerName] = provider;
        return provider;
    }

    public async Task<ILlmProvider> CreateAvailableProviderAsync(CancellationToken cancellationToken = default)
    {
        // Try the default provider first
        try
        {
            var defaultProvider = CreateProvider();
            if (await defaultProvider.IsAvailableAsync(cancellationToken))
            {
                _logger.LogInformation("Using default provider: {Provider}", defaultProvider.Name);
                return defaultProvider;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Default provider not available");
        }

        // Try other configured providers
        foreach (var (name, config) in _options.Value.Providers)
        {
            if (!config.Enabled || name.Equals(_options.Value.DefaultProvider, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var provider = CreateProvider(name);
                if (await provider.IsAvailableAsync(cancellationToken))
                {
                    _logger.LogInformation("Fallback to provider: {Provider}", provider.Name);
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

    private ILlmProvider CreateAzureProvider()
    {
        // TODO: Implement Azure OpenAI provider
        throw new NotImplementedException("Azure OpenAI provider not yet implemented");
    }

    private ILlmProvider CreateLocalProvider()
    {
        // TODO: Implement local/Ollama provider
        throw new NotImplementedException("Local/Ollama provider not yet implemented");
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