using System.Text.Json;
using System.Text.Json.Nodes;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// The outcome category of parsing a structured (JSON-schema-governed) LLM response.
/// </summary>
public enum StructuredOutputStatus
{
    /// <summary>The response parsed as JSON and conforms to the schema.</summary>
    Success,

    /// <summary>
    /// The provider refused the request (for example a content filter), so there is no
    /// payload to parse.
    /// </summary>
    Refusal,

    /// <summary>The provider stopped because of a length limit; the payload may be incomplete.</summary>
    Truncated,

    /// <summary>The response content is not well-formed JSON.</summary>
    MalformedJson,

    /// <summary>The response is well-formed JSON but does not conform to the schema.</summary>
    SchemaMismatch,

    /// <summary>The supplied schema itself failed <see cref="JsonSchemaPolicy"/> screening.</summary>
    InvalidSchema,

    /// <summary>The provider cannot produce the requested structured output format at all.</summary>
    UnsupportedCapability,
}

/// <summary>
/// Result envelope for parsed structured output. Distinguishes success from provider refusal,
/// truncation, malformed JSON, schema mismatch, an invalid schema, and unsupported capability,
/// and carries the parsed value plus diagnostics for each case.
/// </summary>
/// <remarks>
/// This complements <c>Andy.Llm.Parsing.StructuredLlmResponse</c>, which models provider
/// tool-call envelopes; <see cref="StructuredOutputResult"/> models the JSON-schema
/// structured-output path and is produced by <see cref="StructuredOutputParser"/>.
/// Instances are immutable and created through the static factory methods or the parser.
/// </remarks>
public sealed class StructuredOutputResult
{
    private StructuredOutputResult(
        StructuredOutputStatus status,
        JsonNode? value,
        string? rawContent,
        string? finishReason,
        string? refusalReason,
        string? parseErrorDetail,
        IReadOnlyList<SchemaValidationIssue>? schemaIssues,
        IReadOnlyList<JsonSchemaIssue>? policyIssues,
        string? diagnosticMessage)
    {
        Status = status;
        Value = value;
        RawContent = rawContent;
        FinishReason = finishReason;
        RefusalReason = refusalReason;
        ParseErrorDetail = parseErrorDetail;
        SchemaIssues = schemaIssues ?? Array.Empty<SchemaValidationIssue>();
        PolicyIssues = policyIssues ?? Array.Empty<JsonSchemaIssue>();
        DiagnosticMessage = diagnosticMessage;
    }

    /// <summary>The outcome category.</summary>
    public StructuredOutputStatus Status { get; }

    /// <summary>True only when <see cref="Status"/> is <see cref="StructuredOutputStatus.Success"/>.</summary>
    public bool IsSuccess => Status == StructuredOutputStatus.Success;

    /// <summary>The parsed payload on success; null otherwise.</summary>
    public JsonNode? Value { get; }

    /// <summary>The raw text content that was parsed, when available.</summary>
    public string? RawContent { get; }

    /// <summary>The provider's raw finish reason (for example <c>stop</c>, <c>length</c>), when known.</summary>
    public string? FinishReason { get; }

    /// <summary>The refusal reason supplied by the provider or caller, for refusals.</summary>
    public string? RefusalReason { get; }

    /// <summary>The JSON parser's error detail, for malformed payloads.</summary>
    public string? ParseErrorDetail { get; }

    /// <summary>Schema mismatches found in the payload, for <see cref="StructuredOutputStatus.SchemaMismatch"/>.</summary>
    public IReadOnlyList<SchemaValidationIssue> SchemaIssues { get; }

    /// <summary>Policy issues found in the schema itself, for <see cref="StructuredOutputStatus.InvalidSchema"/>.</summary>
    public IReadOnlyList<JsonSchemaIssue> PolicyIssues { get; }

    /// <summary>A short human-readable summary of the outcome.</summary>
    public string? DiagnosticMessage { get; }

    /// <summary>
    /// Deserializes the successful <see cref="Value"/> into <typeparamref name="T"/>.
    /// Throws <see cref="InvalidOperationException"/> when the result is not a success.
    /// </summary>
    public T GetValue<T>(JsonSerializerOptions? options = null)
    {
        if (!IsSuccess || Value is null)
        {
            throw new InvalidOperationException(
                $"Cannot materialize a value from a '{Status}' structured output result.");
        }

        return Value.Deserialize<T>(options)
               ?? throw new InvalidOperationException(
                   $"The structured output value could not be deserialized as {typeof(T).Name}.");
    }

    /// <summary>Creates a success result carrying the parsed payload.</summary>
    public static StructuredOutputResult Success(JsonNode value, string? rawContent, string? finishReason) =>
        new(StructuredOutputStatus.Success, value, rawContent, finishReason, null, null, null, null,
            "The response parsed as JSON and conforms to the schema.");

    /// <summary>Creates a refusal result.</summary>
    public static StructuredOutputResult Refused(string? reason, string? rawContent, string? finishReason) =>
        new(StructuredOutputStatus.Refusal, null, rawContent, finishReason,
            reason, null, null, null,
            string.IsNullOrEmpty(reason) ? "The provider refused the request." : $"The provider refused the request: {reason}");

    /// <summary>Creates a truncation result.</summary>
    public static StructuredOutputResult TruncatedByLength(string? rawContent, string? finishReason) =>
        new(StructuredOutputStatus.Truncated, null, rawContent, finishReason, null, null, null, null,
            $"The provider stopped at a length limit (finish reason '{finishReason ?? "length"}'); the payload may be incomplete.");

    /// <summary>Creates a malformed-JSON result.</summary>
    public static StructuredOutputResult Malformed(string? rawContent, string? finishReason, string parseErrorDetail) =>
        new(StructuredOutputStatus.MalformedJson, null, rawContent, finishReason, null, parseErrorDetail, null, null,
            $"The response content is not valid JSON: {parseErrorDetail}");

    /// <summary>Creates a schema-mismatch result.</summary>
    public static StructuredOutputResult Mismatch(
        string? rawContent, string? finishReason, IReadOnlyList<SchemaValidationIssue> issues) =>
        new(StructuredOutputStatus.SchemaMismatch, null, rawContent, finishReason, null, null, issues, null,
            $"The response is valid JSON but violates the schema ({issues.Count} issue(s)); first: {issues[0]}.");

    /// <summary>Creates an invalid-schema result.</summary>
    public static StructuredOutputResult InvalidSchema(IReadOnlyList<JsonSchemaIssue> policyIssues) =>
        new(StructuredOutputStatus.InvalidSchema, null, null, null, null, null, null, policyIssues,
            $"The supplied schema failed policy screening ({policyIssues.Count} issue(s)); first: {policyIssues[0]}.");

    /// <summary>Creates an unsupported-capability result.</summary>
    public static StructuredOutputResult Unsupported(string capability, string? detail = null) =>
        new(StructuredOutputStatus.UnsupportedCapability, null, null, null, null, null, null, null,
            string.IsNullOrEmpty(detail)
                ? $"The provider cannot produce the requested structured output ({capability})."
                : $"The provider cannot produce the requested structured output ({capability}): {detail}");
}
