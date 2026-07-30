namespace Andy.Llm.StructuredOutput;

/// <summary>
/// Configurable limits enforced by <see cref="JsonSchemaPolicy"/> when screening a
/// JSON Schema before it is used for structured output. Defaults are chosen so that
/// realistic request/response schemas pass comfortably while pathological documents
/// (multi-megabyte blobs, deeply nested or keyword-flooded schemas, recursive
/// references) are rejected with typed issues instead of causing unbounded work.
/// </summary>
public sealed class JsonSchemaLimits
{
    /// <summary>
    /// The default limits: 64 KiB schema size, 32 levels of JSON nesting,
    /// 500 object members across the whole schema document, no <c>$ref</c>.
    /// </summary>
    public static JsonSchemaLimits Default { get; } = new();

    /// <summary>
    /// Maximum size of the schema document in UTF-8 bytes. Defaults to 64 KiB.
    /// </summary>
    public int MaxSchemaBytes { get; init; } = 64 * 1024;

    /// <summary>
    /// Maximum JSON nesting depth of the schema document (root object = depth 1).
    /// Defaults to 32.
    /// </summary>
    public int MaxDepth { get; init; } = 32;

    /// <summary>
    /// Maximum number of object members (property occurrences) counted across the
    /// entire schema document, including annotation values such as
    /// <c>default</c> and <c>examples</c>. Defaults to 500.
    /// </summary>
    public int MaxKeywords { get; init; } = 500;
}
