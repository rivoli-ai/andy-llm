using System.Text;
using System.Text.Json;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// Screens a JSON Schema before it is used for structured output, enforcing the size and
/// complexity limits in <see cref="JsonSchemaLimits"/> and rejecting vocabulary outside the
/// supported subset. Validation is synchronous, allocation-bounded, and never throws:
/// every failure is reported as a typed <see cref="JsonSchemaIssue"/>.
/// </summary>
/// <remarks>
/// <para>
/// Supported subset: <c>type</c>, <c>enum</c>, <c>const</c>, <c>properties</c>,
/// <c>required</c>, <c>additionalProperties</c>, <c>items</c> (single-schema form only),
/// the basic scalar constraints (<c>minimum</c>, <c>maximum</c>, <c>exclusiveMinimum</c>,
/// <c>exclusiveMaximum</c>, <c>multipleOf</c>, <c>minLength</c>, <c>maxLength</c>,
/// <c>pattern</c>, <c>minItems</c>, <c>maxItems</c>, <c>uniqueItems</c>,
/// <c>minProperties</c>, <c>maxProperties</c>), and inert annotations
/// (<c>$schema</c>, <c>$id</c>, <c>$comment</c>, <c>title</c>, <c>description</c>,
/// <c>default</c>, <c>examples</c>, <c>format</c>).
/// </para>
/// <para>
/// <c>$ref</c> is rejected outright (<see cref="JsonSchemaIssueKind.RecursiveReference"/>):
/// schemas must be self-contained so that neither policy screening nor instance validation
/// can recurse unboundedly through recursive or cyclic references. Consequently
/// <c>$defs</c>/<c>definitions</c> are unsupported as well. Composition and conditional
/// keywords (<c>allOf</c>, <c>anyOf</c>, <c>oneOf</c>, <c>not</c>, <c>if</c>/<c>then</c>/<c>else</c>),
/// <c>patternProperties</c>, <c>contains</c>, and <c>propertyNames</c> are unsupported.
/// </para>
/// </remarks>
public static class JsonSchemaPolicy
{
    private const string RootPath = "$";

    private static readonly HashSet<string> SupportedKeywordSet = new(StringComparer.Ordinal)
    {
        // Inert annotations (ignored during instance validation)
        "$schema", "$id", "$comment", "title", "description", "default", "examples", "format",
        // Core validation keywords
        "type", "enum", "const",
        // Object and array structure
        "properties", "required", "additionalProperties", "items",
        // Numeric constraints
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        // String constraints
        "minLength", "maxLength", "pattern",
        // Array constraints
        "minItems", "maxItems", "uniqueItems",
        // Object constraints
        "minProperties", "maxProperties",
    };

    private static readonly HashSet<string> KnownTypeNames = new(StringComparer.Ordinal)
    {
        "object", "array", "string", "number", "integer", "boolean", "null",
    };

    /// <summary>
    /// The keywords accepted by the supported JSON Schema subset. Any other keyword in a
    /// schema position produces a <see cref="JsonSchemaIssueKind.UnsupportedKeyword"/> issue.
    /// </summary>
    public static IReadOnlySet<string> SupportedKeywords => SupportedKeywordSet;

    /// <summary>
    /// Validates <paramref name="schemaJson"/> against the configured limits and the
    /// supported keyword subset. Returns a typed result; never throws.
    /// </summary>
    /// <param name="schemaJson">The JSON Schema document as text.</param>
    /// <param name="limits">Limits to enforce; <see cref="JsonSchemaLimits.Default"/> when null.</param>
    public static JsonSchemaValidationResult Validate(string? schemaJson, JsonSchemaLimits? limits = null)
    {
        limits ??= JsonSchemaLimits.Default;
        var issues = new List<JsonSchemaIssue>();

        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            issues.Add(new JsonSchemaIssue(
                JsonSchemaIssueKind.MalformedSchema, RootPath, "The schema document is null, empty, or whitespace."));
            return new JsonSchemaValidationResult(issues);
        }

        var byteCount = Encoding.UTF8.GetByteCount(schemaJson);
        if (byteCount > limits.MaxSchemaBytes)
        {
            issues.Add(new JsonSchemaIssue(
                JsonSchemaIssueKind.TooLarge, RootPath,
                $"The schema is {byteCount} bytes, exceeding the {limits.MaxSchemaBytes}-byte limit."));
            return new JsonSchemaValidationResult(issues);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException ex)
        {
            issues.Add(new JsonSchemaIssue(
                JsonSchemaIssueKind.MalformedSchema, RootPath, $"The schema is not valid JSON: {ex.Message}"));
            return new JsonSchemaValidationResult(issues);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                // Boolean schemas are legal JSON Schema but outside the supported subset:
                // the structured-output contract always describes a concrete shape.
                issues.Add(new JsonSchemaIssue(
                    JsonSchemaIssueKind.MalformedSchema, RootPath,
                    "The root schema must be a JSON object; boolean and scalar schemas are not supported."));
                return new JsonSchemaValidationResult(issues);
            }

            MeasureDocument(document.RootElement, limits, issues);
            if (!issues.Any(i => i.Kind is JsonSchemaIssueKind.TooManyKeywords))
            {
                // Skip the vocabulary walk once the document is already rejected for size:
                // the member count alone tells the caller the schema is too complex to trust.
                CheckSchemaNode(document.RootElement, RootPath, issues);
            }
        }

        return new JsonSchemaValidationResult(issues);
    }

    /// <summary>
    /// Walks the entire document once, enforcing the nesting-depth limit and counting every
    /// object member toward the keyword limit. Stops descending a branch once it is too deep,
    /// and abandons the walk entirely once the member count is exceeded.
    /// </summary>
    private static void MeasureDocument(JsonElement element, JsonSchemaLimits limits, List<JsonSchemaIssue> issues)
    {
        var state = new MeasureState();

        void Visit(JsonElement current, string path, int depth)
        {
            if (state.Aborted)
            {
                return;
            }

            if (current.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return;
            }

            if (depth > limits.MaxDepth)
            {
                issues.Add(new JsonSchemaIssue(
                    JsonSchemaIssueKind.TooDeep, path,
                    $"Nesting depth {depth} exceeds the limit of {limits.MaxDepth}."));
                return;
            }

            if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                {
                    state.MemberCount++;
                    if (state.MemberCount > limits.MaxKeywords)
                    {
                        issues.Add(new JsonSchemaIssue(
                            JsonSchemaIssueKind.TooManyKeywords, path,
                            $"The schema contains more than {limits.MaxKeywords} object members."));
                        state.Aborted = true;
                        return;
                    }

                    Visit(property.Value, path + "." + property.Name, depth + 1);
                    if (state.Aborted)
                    {
                        return;
                    }
                }
            }
            else
            {
                var index = 0;
                foreach (var item in current.EnumerateArray())
                {
                    Visit(item, $"{path}[{index}]", depth + 1);
                    if (state.Aborted)
                    {
                        return;
                    }

                    index++;
                }
            }
        }

        Visit(element, RootPath, 1);
    }

    /// <summary>
    /// Walks schema positions only (root, <c>properties</c> values, <c>items</c>,
    /// object-valued <c>additionalProperties</c>), checking every keyword against the
    /// supported subset and validating the shape of structural keywords. Data positions
    /// such as <c>enum</c>, <c>const</c>, <c>default</c>, and <c>examples</c> values are
    /// never interpreted as schemas.
    /// </summary>
    private static void CheckSchemaNode(JsonElement schema, string path, List<JsonSchemaIssue> issues)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            // Boolean subschemas (e.g. "additionalProperties": false) are accepted in place.
            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new JsonSchemaIssue(
                JsonSchemaIssueKind.MalformedSchema, path,
                "A schema must be a JSON object (or a boolean subschema)."));
            return;
        }

        foreach (var keyword in schema.EnumerateObject())
        {
            var keywordPath = path + "." + keyword.Name;

            if (keyword.Name == "$ref")
            {
                issues.Add(new JsonSchemaIssue(
                    JsonSchemaIssueKind.RecursiveReference, keywordPath,
                    "'$ref' is not supported: schemas must be self-contained so validation cannot recurse unboundedly."));
                continue;
            }

            if (!SupportedKeywordSet.Contains(keyword.Name))
            {
                issues.Add(new JsonSchemaIssue(
                    JsonSchemaIssueKind.UnsupportedKeyword, keywordPath,
                    $"The keyword '{keyword.Name}' is outside the supported subset."));
                continue;
            }

            switch (keyword.Name)
            {
                case "type":
                    CheckTypeKeyword(keyword.Value, keywordPath, issues);
                    break;
                case "enum":
                    if (keyword.Value.ValueKind != JsonValueKind.Array || keyword.Value.GetArrayLength() == 0)
                    {
                        issues.Add(new JsonSchemaIssue(
                            JsonSchemaIssueKind.MalformedSchema, keywordPath, "'enum' must be a non-empty array."));
                    }

                    break;
                case "required":
                    if (keyword.Value.ValueKind != JsonValueKind.Array ||
                        keyword.Value.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                    {
                        issues.Add(new JsonSchemaIssue(
                            JsonSchemaIssueKind.MalformedSchema, keywordPath, "'required' must be an array of strings."));
                    }

                    break;
                case "properties":
                    if (keyword.Value.ValueKind != JsonValueKind.Object)
                    {
                        issues.Add(new JsonSchemaIssue(
                            JsonSchemaIssueKind.MalformedSchema, keywordPath, "'properties' must be an object."));
                        break;
                    }

                    foreach (var property in keyword.Value.EnumerateObject())
                    {
                        CheckSchemaNode(property.Value, keywordPath + "." + property.Name, issues);
                    }

                    break;
                case "items":
                    if (keyword.Value.ValueKind == JsonValueKind.Array)
                    {
                        issues.Add(new JsonSchemaIssue(
                            JsonSchemaIssueKind.MalformedSchema, keywordPath,
                            "Only the single-schema form of 'items' is supported; tuple validation is not."));
                        break;
                    }

                    CheckSchemaNode(keyword.Value, keywordPath, issues);
                    break;
                case "additionalProperties":
                    CheckSchemaNode(keyword.Value, keywordPath, issues);
                    break;
            }
        }
    }

    private static void CheckTypeKeyword(JsonElement value, string path, List<JsonSchemaIssue> issues)
    {
        static bool IsKnownTypeName(JsonElement element) =>
            element.ValueKind == JsonValueKind.String && KnownTypeNames.Contains(element.GetString()!);

        var valid = value.ValueKind == JsonValueKind.String
            ? IsKnownTypeName(value)
            : value.ValueKind == JsonValueKind.Array &&
              value.GetArrayLength() > 0 &&
              value.EnumerateArray().All(IsKnownTypeName);

        if (!valid)
        {
            issues.Add(new JsonSchemaIssue(
                JsonSchemaIssueKind.MalformedSchema, path,
                "'type' must be one of 'object', 'array', 'string', 'number', 'integer', 'boolean', 'null', " +
                "or a non-empty array of those names."));
        }
    }

    private sealed class MeasureState
    {
        public int MemberCount;
        public bool Aborted;
    }
}
