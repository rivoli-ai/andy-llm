namespace Andy.Llm.StructuredOutput;

/// <summary>
/// The typed kind of a <see cref="SchemaValidationIssue"/> raised when a JSON document
/// does not conform to a schema.
/// </summary>
public enum SchemaMismatchKind
{
    /// <summary>The value's JSON type does not match the schema's <c>type</c>.</summary>
    TypeMismatch,

    /// <summary>A property listed in the schema's <c>required</c> is absent.</summary>
    MissingRequiredProperty,

    /// <summary>
    /// The object carries a property not listed in <c>properties</c> while
    /// <c>additionalProperties</c> is <c>false</c>.
    /// </summary>
    UnexpectedProperty,

    /// <summary>The value is not one of the schema's <c>enum</c> (or <c>const</c>) values.</summary>
    EnumViolation,

    /// <summary>
    /// The value violates a scalar or structural constraint (<c>minimum</c>, <c>maxLength</c>,
    /// <c>pattern</c>, <c>minItems</c>, <c>uniqueItems</c>, and similar).
    /// </summary>
    ConstraintViolation,
}

/// <summary>
/// A single way in which a JSON document fails to conform to a schema.
/// </summary>
public sealed class SchemaValidationIssue
{
    /// <summary>The typed kind of the mismatch.</summary>
    public SchemaMismatchKind Kind { get; }

    /// <summary>
    /// JSON-path-style location of the offending value within the instance document
    /// (for example <c>$.tasks[0].id</c>). <c>$</c> is the document root.
    /// </summary>
    public string Path { get; }

    /// <summary>A human-readable description of the mismatch.</summary>
    public string Message { get; }

    /// <summary>Creates an issue with the given kind, path, and message.</summary>
    public SchemaValidationIssue(SchemaMismatchKind kind, string path, string message)
    {
        Kind = kind;
        Path = path;
        Message = message;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} at {Path}: {Message}";
}

/// <summary>
/// The outcome of validating a JSON document against a schema with
/// <see cref="JsonSchemaValidator"/>. Validation never throws; an empty issue list
/// means the document conforms.
/// </summary>
public sealed class SchemaValidationResult
{
    /// <summary>True when the document conforms to the schema.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>The mismatches found, in document order.</summary>
    public IReadOnlyList<SchemaValidationIssue> Issues { get; }

    /// <summary>Creates a result wrapping the given issues.</summary>
    public SchemaValidationResult(IReadOnlyList<SchemaValidationIssue> issues)
    {
        Issues = issues;
    }
}
