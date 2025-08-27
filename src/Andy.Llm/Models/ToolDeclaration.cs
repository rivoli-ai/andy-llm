namespace Andy.Llm.Models;

/// <summary>
/// Declares a tool/function available to the LLM
/// </summary>
public class ToolDeclaration
{
    /// <summary>
    /// The name of the tool
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Description of what the tool does
    /// </summary>
    public required string Description { get; set; }

    /// <summary>
    /// JSON Schema of the tool's parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Whether this tool is required
    /// </summary>
    public bool Required { get; set; }
}