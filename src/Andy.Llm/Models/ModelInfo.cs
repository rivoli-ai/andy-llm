namespace Andy.Llm.Models;

/// <summary>
/// Represents information about an available language model.
/// </summary>
public class ModelInfo
{
    /// <summary>
    /// Gets or sets the unique identifier of the model.
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the display name of the model.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the provider of the model.
    /// </summary>
    public string Provider { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the description of the model.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Gets or sets the creation date of the model.
    /// </summary>
    public DateTime? Created { get; set; }
    
    /// <summary>
    /// Gets or sets the model family or base model.
    /// </summary>
    public string? Family { get; set; }
    
    /// <summary>
    /// Gets or sets the size of the model in parameters (e.g., "7B", "70B").
    /// </summary>
    public string? ParameterSize { get; set; }
    
    /// <summary>
    /// Gets or sets the maximum context length in tokens.
    /// </summary>
    public int? MaxTokens { get; set; }
    
    /// <summary>
    /// Gets or sets whether the model supports function calling.
    /// </summary>
    public bool? SupportsFunctions { get; set; }
    
    /// <summary>
    /// Gets or sets whether the model supports vision/image inputs.
    /// </summary>
    public bool? SupportsVision { get; set; }
    
    /// <summary>
    /// Gets or sets whether this is a fine-tuned model.
    /// </summary>
    public bool? IsFineTuned { get; set; }
    
    /// <summary>
    /// Gets or sets additional metadata about the model.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}