namespace Andy.Llm.Configuration;

/// <summary>
/// Provider-specific configuration
/// </summary>
public class ProviderConfig
{
    /// <summary>
    /// API key for the provider
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL for the API endpoint
    /// </summary>
    public string? ApiBase { get; set; }

    /// <summary>
    /// Default model for this provider
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Organization ID (for OpenAI)
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// API version (for Azure OpenAI)
    /// </summary>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Deployment name (for Azure OpenAI)
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// Additional provider-specific settings
    /// </summary>
    public Dictionary<string, object>? AdditionalSettings { get; set; }

    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}
