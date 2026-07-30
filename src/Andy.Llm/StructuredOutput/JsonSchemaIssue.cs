namespace Andy.Llm.StructuredOutput;

/// <summary>
/// The typed kind of a <see cref="JsonSchemaIssue"/> raised by <see cref="JsonSchemaPolicy"/>.
/// </summary>
public enum JsonSchemaIssueKind
{
    /// <summary>The schema document exceeds the configured byte limit.</summary>
    TooLarge,

    /// <summary>The schema document nests deeper than the configured depth limit.</summary>
    TooDeep,

    /// <summary>The schema document contains more object members than the configured limit.</summary>
    TooManyKeywords,

    /// <summary>
    /// The schema uses <c>$ref</c>. References are rejected outright: the structured-output
    /// subset requires self-contained schemas so that validation can never recurse
    /// unboundedly through recursive or cyclic references.
    /// </summary>
    RecursiveReference,

    /// <summary>
    /// The schema uses a keyword outside the supported subset
    /// (see <see cref="JsonSchemaPolicy.SupportedKeywords"/>).
    /// </summary>
    UnsupportedKeyword,

    /// <summary>The document is not well-formed JSON, or a supported keyword has an invalid shape.</summary>
    MalformedSchema,
}

/// <summary>
/// A single issue found while screening a JSON Schema with <see cref="JsonSchemaPolicy"/>.
/// </summary>
public sealed class JsonSchemaIssue
{
    /// <summary>The typed kind of the issue.</summary>
    public JsonSchemaIssueKind Kind { get; }

    /// <summary>
    /// JSON-path-style location of the offending node within the schema document
    /// (for example <c>$.properties.tasks.items</c>). <c>$</c> when the issue
    /// concerns the document as a whole.
    /// </summary>
    public string Path { get; }

    /// <summary>A human-readable description of the issue.</summary>
    public string Message { get; }

    /// <summary>Creates an issue with the given kind, path, and message.</summary>
    public JsonSchemaIssue(JsonSchemaIssueKind kind, string path, string message)
    {
        Kind = kind;
        Path = path;
        Message = message;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} at {Path}: {Message}";
}

/// <summary>
/// The outcome of screening a JSON Schema with <see cref="JsonSchemaPolicy"/>.
/// Validation never throws; malformed input is reported as issues.
/// </summary>
public sealed class JsonSchemaValidationResult
{
    /// <summary>True when the schema passed every policy check.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>The issues found, in the order they were detected.</summary>
    public IReadOnlyList<JsonSchemaIssue> Issues { get; }

    /// <summary>Creates a result wrapping the given issues.</summary>
    public JsonSchemaValidationResult(IReadOnlyList<JsonSchemaIssue> issues)
    {
        Issues = issues;
    }
}
