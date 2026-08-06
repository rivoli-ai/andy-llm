using System.Text.Json;
using System.Text.Json.Nodes;
using Andy.Model.Llm;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// Turns raw LLM output into a <see cref="StructuredOutputResult"/> envelope. The pipeline is
/// synchronous and total: screen the schema (<see cref="JsonSchemaPolicy"/>), classify the
/// provider outcome (refusal, truncation), parse the JSON, then validate the instance against
/// the supported schema subset (<see cref="JsonSchemaValidator"/>).
/// </summary>
public static class StructuredOutputParser
{
    /// <summary>
    /// Finish reasons that indicate the provider stopped at a length/token limit.
    /// Compared case-insensitively.
    /// </summary>
    private static readonly HashSet<string> TruncationFinishReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "length", "max_tokens", "max_output_tokens",
    };

    /// <summary>
    /// Finish reasons that indicate the provider refused to answer. Compared case-insensitively.
    /// </summary>
    private static readonly HashSet<string> RefusalFinishReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "content_filter", "refusal",
    };

    /// <summary>
    /// Classifies and validates structured output from raw content plus the provider's finish reason.
    /// </summary>
    /// <param name="content">The raw text content returned by the provider.</param>
    /// <param name="finishReason">The provider's raw finish reason, if known.</param>
    /// <param name="schemaJson">The JSON Schema the content must conform to.</param>
    /// <param name="limits">Schema policy limits; <see cref="JsonSchemaLimits.Default"/> when null.</param>
    /// <param name="refusalReason">
    /// An explicit refusal reason when the caller already knows the provider refused
    /// (for example from provider-specific refusal fields not visible on the content).
    /// </param>
    public static StructuredOutputResult Parse(
        string? content,
        string? finishReason,
        string schemaJson,
        JsonSchemaLimits? limits = null,
        string? refusalReason = null)
    {
        var policyResult = JsonSchemaPolicy.Validate(schemaJson, limits);
        if (!policyResult.IsValid)
        {
            return StructuredOutputResult.InvalidSchema(policyResult.Issues);
        }

        if (!string.IsNullOrEmpty(refusalReason) ||
            (finishReason != null && RefusalFinishReasons.Contains(finishReason)))
        {
            return StructuredOutputResult.Refused(refusalReason ?? finishReason, content, finishReason);
        }

        if (finishReason != null && TruncationFinishReasons.Contains(finishReason))
        {
            return StructuredOutputResult.TruncatedByLength(content, finishReason);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return StructuredOutputResult.Malformed(content, finishReason, "The response content is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            return StructuredOutputResult.Malformed(content, finishReason, ex.Message);
        }

        using (document)
        {
            var validation = JsonSchemaValidator.Validate(document.RootElement, schemaJson);
            if (!validation.IsValid)
            {
                return StructuredOutputResult.Mismatch(content, finishReason, validation.Issues);
            }

            // The content already parsed once successfully; this cannot fail.
            var value = JsonNode.Parse(content)!;
            return StructuredOutputResult.Success(value, content, finishReason);
        }
    }

    /// <summary>
    /// Classifies and validates structured output from an <see cref="LlmResponse"/>.
    /// Reads <see cref="LlmResponse.Content"/> and <see cref="LlmResponse.FinishReason"/>;
    /// provider-specific refusal signals that do not surface there can be passed via
    /// <paramref name="refusalReason"/>.
    /// </summary>
    public static StructuredOutputResult FromResponse(
        LlmResponse response,
        string schemaJson,
        JsonSchemaLimits? limits = null,
        string? refusalReason = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Parse(response.Content, response.FinishReason, schemaJson, limits, refusalReason);
    }
}
