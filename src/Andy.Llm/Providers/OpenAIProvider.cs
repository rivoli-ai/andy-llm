using System.ClientModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Llm.Providers;
using Andy.Llm.Configuration;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace Andy.Llm.Providers;

/// <summary>
/// OpenAI provider implementation that supports multiple API protocols via the strategy pattern.
///
/// Supports two API protocols:
/// - Chat Completions API (/v1/chat/completions): For GPT-4, GPT-4o, and standard models
/// - Responses API (/v1/responses): For Codex models (codex-mini-latest, gpt-5-codex, etc.)
///
/// The API protocol is selected based on:
/// 1. Explicit <see cref="ProviderConfig.ApiType"/> setting ("chat-completions" or "responses")
/// 2. Auto-detection from model name (models containing "codex" use Responses API)
/// 3. Default: Chat Completions API
/// </summary>
public class OpenAIProvider : Andy.Model.Llm.ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;
    private readonly ProviderConfig _config;
    private readonly string _defaultModel;
    private readonly string _configName;
    private readonly IOpenAIApiStrategy _strategy;

    /// <summary>
    /// Gets the name of this provider.
    /// </summary>
    public string Name => _configName;

    /// <summary>
    /// Gets the API type being used by this provider instance.
    /// </summary>
    public string ApiType => _strategy.ApiType;

    /// <summary>
    /// Gets the default model for this provider instance.
    /// </summary>
    public string DefaultModel => _defaultModel;

    /// <summary>
    /// Initializes a new instance of the OpenAI provider with a specific configuration
    /// </summary>
    /// <param name="config">The provider configuration</param>
    /// <param name="configName">The configuration name (e.g., "openai/codex-mini")</param>
    /// <param name="logger">Logger</param>
    /// <param name="httpClientFactory">Optional HTTP client factory (for models endpoint)</param>
    public OpenAIProvider(
        ProviderConfig config,
        string configName,
        ILogger<OpenAIProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _configName = configName ?? "OpenAI";

        // Validate ApiKey is configured
        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            throw new InvalidOperationException($"OpenAI API key not configured for '{configName}'");
        }

        // Validate ApiBase is configured
        if (string.IsNullOrEmpty(_config.ApiBase))
        {
            throw new InvalidOperationException($"OpenAI API base URL not configured for '{configName}'");
        }

        // Validate Model is configured
        if (string.IsNullOrEmpty(_config.Model))
        {
            throw new InvalidOperationException($"OpenAI model not configured for '{configName}'");
        }

        _defaultModel = _config.Model;

        // Create HTTP client for models endpoint and Responses API
        _httpClient = httpClientFactory?.CreateClient("OpenAI") ?? new HttpClient();
        var baseUrl = _config.ApiBase.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        if (!string.IsNullOrEmpty(_config.Organization))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _config.Organization);
        }

        // Select API strategy based on config or auto-detection
        _strategy = CreateStrategy();

        _logger.LogInformation("OpenAI provider initialized - Config: {ConfigName}, Model: {Model}, API type: {ApiType}",
            configName, _defaultModel, _strategy.ApiType);
    }

    /// <summary>
    /// Initializes a new instance of the OpenAI provider (backward compatibility constructor)
    /// </summary>
    /// <param name="options">Llm options</param>
    /// <param name="logger">Logger</param>
    /// <param name="httpClientFactory">Optional HTTP client factory (for models endpoint)</param>
    public OpenAIProvider(
        IOptions<LlmOptions> options,
        ILogger<OpenAIProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var llmOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // Find the first OpenAI configuration (supports hierarchical names like "openai/latest-small")
        var openAiConfig = llmOptions.Providers
            .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key, "openai", StringComparison.OrdinalIgnoreCase));

        if (openAiConfig.Value == null)
        {
            // Fallback: try simple "openai" key for backward compatibility
            if (!llmOptions.Providers.TryGetValue("openai", out var config))
            {
                throw new InvalidOperationException("OpenAI provider configuration not found. Ensure at least one provider configuration has Provider=\"openai\" or use the key \"openai\".");
            }
            _config = config;
            _configName = "openai";
        }
        else
        {
            _config = openAiConfig.Value;
            _configName = openAiConfig.Key;
        }

        if (string.IsNullOrEmpty(_config.ApiKey))
        {
            // Try environment variable
            _config.ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(_config.ApiKey))
            {
                throw new InvalidOperationException("OpenAI API key not configured");
            }
        }

        // Validate ApiBase is configured
        if (string.IsNullOrEmpty(_config.ApiBase))
        {
            throw new InvalidOperationException("OpenAI API base URL not configured");
        }

        // Validate Model is configured
        if (string.IsNullOrEmpty(_config.Model))
        {
            throw new InvalidOperationException("OpenAI model not configured");
        }

        _defaultModel = _config.Model;

        // Create HTTP client for models endpoint and Responses API
        _httpClient = httpClientFactory?.CreateClient("OpenAI") ?? new HttpClient();
        var baseUrl = _config.ApiBase.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        if (!string.IsNullOrEmpty(_config.Organization))
        {
            _httpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _config.Organization);
        }

        // Select API strategy based on config or auto-detection
        _strategy = CreateStrategy();

        _logger.LogInformation("OpenAI provider initialized with model: {Model}, API type: {ApiType}",
            _defaultModel, _strategy.ApiType);
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return _strategy.IsAvailableAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        return _strategy.CompleteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        return _strategy.StreamCompleteAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsResponse = JsonSerializer.Deserialize<OpenAIModelsResponse>(content);

            if (modelsResponse?.Data == null)
            {
                return Enumerable.Empty<ModelInfo>();
            }

            return modelsResponse.Data
                .Select(model => new ModelInfo
                {
                    Id = model.Id ?? string.Empty,
                    Name = model.Id ?? string.Empty,
                    Provider = "openai",
                    UpdatedAt = model.Created.HasValue ? DateTimeOffset.FromUnixTimeSeconds(model.Created.Value) : null,
                    Description = GetModelDescription(model.Id),
                    Family = GetModelFamily(model.Id),
                    ParameterSize = GetParameterSize(model.Id),
                    MaxTokens = GetMaxTokens(model.Id),
                    SupportsFunctions = SupportsFunctionCalling(model.Id),
                    SupportsVision = SupportsVision(model.Id),
                    IsFineTuned = model.Id?.Contains("ft-") ?? false,
                    Metadata = new Dictionary<string, object>
                    {
                        ["owned_by"] = model.OwnedBy ?? "unknown"
                    }
                })
                .OrderBy(m => m.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list OpenAI models");
            return Enumerable.Empty<ModelInfo>();
        }
    }

    #region Strategy Selection

    /// <summary>
    /// Creates the appropriate API strategy based on configuration.
    /// </summary>
    private IOpenAIApiStrategy CreateStrategy()
    {
        var apiType = DetectApiType(_config.ApiType, _defaultModel);

        if (apiType == "responses")
        {
            _logger.LogDebug("Using Responses API strategy for model: {Model}", _defaultModel);
            return new ResponsesApiStrategy(_httpClient, _defaultModel, _logger);
        }

        // Default: Chat Completions via OpenAI SDK
        _logger.LogDebug("Using Chat Completions API strategy for model: {Model}", _defaultModel);
        var openAiOptions = new OpenAIClientOptions();
        if (!string.IsNullOrEmpty(_config.ApiBase))
        {
            openAiOptions.Endpoint = new Uri(_config.ApiBase);
        }
        if (!string.IsNullOrEmpty(_config.Organization))
        {
            openAiOptions.OrganizationId = _config.Organization;
        }

        var openAiClient = new OpenAIClient(new ApiKeyCredential(_config.ApiKey!), openAiOptions);
        var chatClient = openAiClient.GetChatClient(_defaultModel);
        return new ChatCompletionsStrategy(chatClient, _logger);
    }

    /// <summary>
    /// Determines which API type to use based on explicit config or model name auto-detection.
    /// </summary>
    /// <param name="configuredApiType">Explicitly configured API type, or null for auto-detection.</param>
    /// <param name="model">The model name to use for auto-detection.</param>
    /// <returns>The API type string: "chat-completions" or "responses".</returns>
    internal static string DetectApiType(string? configuredApiType, string? model)
    {
        // Explicit config takes precedence
        if (!string.IsNullOrEmpty(configuredApiType))
        {
            return configuredApiType.ToLowerInvariant();
        }

        // Auto-detect from model name
        if (model != null && RequiresResponsesApi(model))
        {
            return "responses";
        }

        return "chat-completions";
    }

    /// <summary>
    /// Checks if a model requires the Responses API based on its name.
    /// Codex models (codex-mini-latest, gpt-5-codex, gpt-5.1-codex-*, gpt-5.2-codex)
    /// only work with the Responses API.
    /// </summary>
    internal static bool RequiresResponsesApi(string model)
    {
        var lower = model.ToLowerInvariant();
        return lower.Contains("codex");
    }

    #endregion

    #region Model Metadata (shared across strategies)

    private static string? GetModelDescription(string? modelId)
    {
        if (modelId == null) return null;

        return modelId switch
        {
            var id when id.Contains("codex") => "Code-optimized model (Responses API)",
            var id when id.StartsWith("gpt-4o") => "Most capable multimodal model",
            var id when id.StartsWith("gpt-4-turbo") => "High performance model with vision support",
            var id when id.StartsWith("gpt-4") => "Advanced reasoning model",
            var id when id.StartsWith("gpt-3.5-turbo") => "Fast and efficient model",
            var id when id.StartsWith("dall-e") => "Image generation model",
            var id when id.StartsWith("whisper") => "Speech recognition model",
            var id when id.StartsWith("tts") => "Text-to-speech model",
            var id when id.StartsWith("text-embedding") => "Embedding model",
            _ => null
        };
    }

    private static string? GetModelFamily(string? modelId)
    {
        if (modelId == null) return null;

        return modelId switch
        {
            var id when id.Contains("codex") => "Codex",
            var id when id.StartsWith("gpt-4o") => "GPT-4o",
            var id when id.StartsWith("gpt-4") => "GPT-4",
            var id when id.StartsWith("gpt-3.5") => "GPT-3.5",
            var id when id.StartsWith("dall-e") => "DALL-E",
            var id when id.StartsWith("whisper") => "Whisper",
            var id when id.StartsWith("tts") => "TTS",
            var id when id.StartsWith("text-embedding") => "Embedding",
            _ => null
        };
    }

    private static string? GetParameterSize(string? modelId)
    {
        if (modelId == null) return null;

        return modelId switch
        {
            var id when id.StartsWith("gpt-4o") => "Large",
            var id when id.StartsWith("gpt-4") => "Large",
            var id when id.StartsWith("gpt-3.5") => "Medium",
            _ => null
        };
    }

    private static int? GetMaxTokens(string? modelId)
    {
        if (modelId == null) return null;

        return modelId switch
        {
            var id when id.Contains("codex") => 200000,
            var id when id.Contains("gpt-4o") => 128000,
            var id when id.Contains("gpt-4-turbo") => 128000,
            var id when id.Contains("gpt-4-32k") => 32768,
            var id when id.Contains("gpt-4") && !id.Contains("32k") => 8192,
            var id when id.Contains("gpt-3.5-turbo-16k") => 16385,
            var id when id.Contains("gpt-3.5-turbo") => 4096,
            _ => null
        };
    }

    private static bool SupportsFunctionCalling(string? modelId)
    {
        if (modelId == null) return false;
        return modelId.StartsWith("gpt-") || modelId.Contains("codex");
    }

    private static bool SupportsVision(string? modelId)
    {
        if (modelId == null) return false;

        return modelId.Contains("gpt-4o") ||
               modelId.Contains("gpt-4-turbo") ||
               modelId.Contains("vision");
    }

    #endregion

    #region Response Types

    private class OpenAIModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAIModel>? Data { get; set; }
    }

    private class OpenAIModel
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("created")]
        public long? Created { get; set; }

        [JsonPropertyName("owned_by")]
        public string? OwnedBy { get; set; }
    }

    #endregion
}
