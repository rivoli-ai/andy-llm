namespace Andy.Llm.Configuration;

/// <summary>
/// Configuration options for LLM providers
/// </summary>
public class LlmOptions
{
    /// <summary>
    /// Default provider to use (openai, azure, cerebras, etc.)
    /// </summary>
    public string DefaultProvider { get; set; } = "openai";

    /// <summary>
    /// Provider-specific configurations
    /// </summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

    /// <summary>
    /// Global default model to use if not specified
    /// </summary>
    public string? DefaultModel { get; set; }

    /// <summary>
    /// Global default temperature
    /// </summary>
    public double DefaultTemperature { get; set; } = 0.7;

    /// <summary>
    /// Global default max tokens
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Enable response caching
    /// </summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>
    /// Cache duration in minutes
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;
}