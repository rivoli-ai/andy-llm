using System.Text.Json;
using System.Text.RegularExpressions;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// Validates a JSON instance document against a JSON Schema restricted to the supported
/// subset declared by <see cref="JsonSchemaPolicy"/>: <c>type</c> (including type arrays),
/// <c>required</c>, <c>properties</c>, <c>additionalProperties: false</c>, <c>items</c>,
/// <c>enum</c>, <c>const</c>, and the basic scalar constraints (<c>minimum</c>,
/// <c>maximum</c>, <c>exclusiveMinimum</c>, <c>exclusiveMaximum</c>, <c>multipleOf</c>,
/// <c>minLength</c>, <c>maxLength</c>, <c>pattern</c>, <c>minItems</c>, <c>maxItems</c>,
/// <c>uniqueItems</c>, <c>minProperties</c>, <c>maxProperties</c>).
/// </summary>
/// <remarks>
/// Out of scope and intentionally ignored: <c>format</c> assertions and all annotations
/// (<c>title</c>, <c>description</c>, <c>default</c>, <c>examples</c>), composition and
/// conditional keywords, references, and boolean-valued <c>exclusiveMinimum</c>/
/// <c>exclusiveMaximum</c> (draft-04 form). Screen schemas with
/// <see cref="JsonSchemaPolicy"/> first; this validator assumes a policy-conforming schema
/// and silently skips keyword shapes it does not understand.
/// </remarks>
public static class JsonSchemaValidator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Validates <paramref name="instance"/> against the given schema. Never throws;
    /// returns a typed result whose issues carry instance-side JSON paths.
    /// </summary>
    public static SchemaValidationResult Validate(JsonElement instance, JsonElement schema)
    {
        var issues = new List<SchemaValidationIssue>();
        ValidateNode(instance, schema, "$", issues);
        return new SchemaValidationResult(issues);
    }

    /// <summary>
    /// Parses <paramref name="schemaJson"/> and validates <paramref name="instance"/> against it.
    /// A schema that fails to parse yields a single <see cref="SchemaMismatchKind.ConstraintViolation"/>
    /// issue at the root describing the problem; screen schemas with
    /// <see cref="JsonSchemaPolicy"/> ahead of time for precise diagnostics.
    /// </summary>
    public static SchemaValidationResult Validate(JsonElement instance, string schemaJson)
    {
        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            return Validate(instance, document.RootElement);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult(new[]
            {
                new SchemaValidationIssue(
                    SchemaMismatchKind.ConstraintViolation, "$",
                    $"The schema is not valid JSON: {ex.Message}"),
            });
        }
    }

    private static void ValidateNode(
        JsonElement instance, JsonElement schema, string path, List<SchemaValidationIssue> issues)
    {
        if (schema.ValueKind == JsonValueKind.True)
        {
            return;
        }

        if (schema.ValueKind == JsonValueKind.False)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, "No value is allowed by this schema."));
            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("enum", out var enumValues) &&
            enumValues.ValueKind == JsonValueKind.Array &&
            !enumValues.EnumerateArray().Any(v => JsonValueEquality.Equals(v, instance)))
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.EnumViolation, path,
                $"Value {Truncate(instance)} is not one of the allowed enum values."));
            return;
        }

        if (schema.TryGetProperty("const", out var constValue) &&
            !JsonValueEquality.Equals(constValue, instance))
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.EnumViolation, path,
                $"Value {Truncate(instance)} does not equal the required constant {Truncate(constValue)}."));
            return;
        }

        if (schema.TryGetProperty("type", out var typeKeyword) && !MatchesType(instance, typeKeyword))
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.TypeMismatch, path,
                $"Expected type {typeKeyword.GetRawText()} but found {instance.ValueKind.ToString().ToLowerInvariant()} " +
                $"({Truncate(instance)})."));
            return;
        }

        switch (instance.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(instance, schema, path, issues);
                break;
            case JsonValueKind.Array:
                ValidateArray(instance, schema, path, issues);
                break;
            case JsonValueKind.String:
                ValidateString(instance, schema, path, issues);
                break;
            case JsonValueKind.Number:
                ValidateNumber(instance, schema, path, issues);
                break;
        }
    }

    private static void ValidateObject(
        JsonElement instance, JsonElement schema, string path, List<SchemaValidationIssue> issues)
    {
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String &&
                    !instance.TryGetProperty(name.GetString()!, out _))
                {
                    issues.Add(new SchemaValidationIssue(
                        SchemaMismatchKind.MissingRequiredProperty, path + "." + name.GetString(),
                        $"Required property '{name.GetString()}' is missing."));
                }
            }
        }

        schema.TryGetProperty("properties", out var properties);
        var hasProperties = properties.ValueKind == JsonValueKind.Object;

        var forbidsAdditional =
            schema.TryGetProperty("additionalProperties", out var additional) &&
            additional.ValueKind == JsonValueKind.False;

        if (hasProperties || forbidsAdditional)
        {
            foreach (var property in instance.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    ValidateNode(property.Value, propertySchema, path + "." + property.Name, issues);
                }
                else if (forbidsAdditional)
                {
                    issues.Add(new SchemaValidationIssue(
                        SchemaMismatchKind.UnexpectedProperty, path + "." + property.Name,
                        $"Property '{property.Name}' is not declared in the schema and 'additionalProperties' is false."));
                }
            }
        }

        if (TryGetInteger(schema, "minProperties", out var minProperties) &&
            instance.EnumerateObject().Count() < minProperties)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"Object has fewer than {minProperties} properties."));
        }

        if (TryGetInteger(schema, "maxProperties", out var maxProperties) &&
            instance.EnumerateObject().Count() > maxProperties)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"Object has more than {maxProperties} properties."));
        }
    }

    private static void ValidateArray(
        JsonElement instance, JsonElement schema, string path, List<SchemaValidationIssue> issues)
    {
        var length = instance.GetArrayLength();

        if (TryGetInteger(schema, "minItems", out var minItems) && length < minItems)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, $"Array has fewer than {minItems} items."));
        }

        if (TryGetInteger(schema, "maxItems", out var maxItems) && length > maxItems)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, $"Array has more than {maxItems} items."));
        }

        if (schema.TryGetProperty("uniqueItems", out var uniqueItems) &&
            uniqueItems.ValueKind == JsonValueKind.True &&
            HasDuplicates(instance))
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, "Array items are not unique."));
        }

        if (schema.TryGetProperty("items", out var itemsSchema) &&
            itemsSchema.ValueKind is JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False)
        {
            var index = 0;
            foreach (var item in instance.EnumerateArray())
            {
                ValidateNode(item, itemsSchema, $"{path}[{index}]", issues);
                index++;
            }
        }
    }

    private static void ValidateString(
        JsonElement instance, JsonElement schema, string path, List<SchemaValidationIssue> issues)
    {
        var value = instance.GetString() ?? string.Empty;

        if (TryGetInteger(schema, "minLength", out var minLength) && value.Length < minLength)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"String is shorter than the minimum length of {minLength}."));
        }

        if (TryGetInteger(schema, "maxLength", out var maxLength) && value.Length > maxLength)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"String is longer than the maximum length of {maxLength}."));
        }

        if (schema.TryGetProperty("pattern", out var pattern) && pattern.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!Regex.IsMatch(value, pattern.GetString()!, RegexOptions.None, PatternTimeout))
                {
                    issues.Add(new SchemaValidationIssue(
                        SchemaMismatchKind.ConstraintViolation, path,
                        $"String does not match the pattern '{pattern.GetString()}'."));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                issues.Add(new SchemaValidationIssue(
                    SchemaMismatchKind.ConstraintViolation, path,
                    $"Pattern '{pattern.GetString()}' could not be evaluated within the time limit."));
            }
            catch (ArgumentException)
            {
                // Invalid regex: a schema-side problem that JsonSchemaPolicy reports; skip here.
            }
        }
    }

    private static void ValidateNumber(
        JsonElement instance, JsonElement schema, string path, List<SchemaValidationIssue> issues)
    {
        if (!instance.TryGetDecimal(out var value))
        {
            return;
        }

        if (TryGetNumber(schema, "minimum", out var minimum) && value < minimum)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, $"Value {value} is below the minimum of {minimum}."));
        }

        if (TryGetNumber(schema, "maximum", out var maximum) && value > maximum)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, $"Value {value} is above the maximum of {maximum}."));
        }

        if (TryGetNumber(schema, "exclusiveMinimum", out var exclusiveMinimum) && value <= exclusiveMinimum)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"Value {value} is not greater than the exclusive minimum of {exclusiveMinimum}."));
        }

        if (TryGetNumber(schema, "exclusiveMaximum", out var exclusiveMaximum) && value >= exclusiveMaximum)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path,
                $"Value {value} is not less than the exclusive maximum of {exclusiveMaximum}."));
        }

        if (TryGetNumber(schema, "multipleOf", out var multipleOf) &&
            multipleOf != 0 && value % multipleOf != 0)
        {
            issues.Add(new SchemaValidationIssue(
                SchemaMismatchKind.ConstraintViolation, path, $"Value {value} is not a multiple of {multipleOf}."));
        }
    }

    private static bool MatchesType(JsonElement instance, JsonElement typeKeyword)
    {
        if (typeKeyword.ValueKind == JsonValueKind.String)
        {
            return MatchesSingleType(instance, typeKeyword.GetString());
        }

        if (typeKeyword.ValueKind == JsonValueKind.Array)
        {
            return typeKeyword.EnumerateArray().Any(t =>
                t.ValueKind == JsonValueKind.String && MatchesSingleType(instance, t.GetString()));
        }

        return true;
    }

    private static bool MatchesSingleType(JsonElement instance, string? typeName) => typeName switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => IsInteger(instance),
        _ => true,
    };

    private static bool IsInteger(JsonElement instance)
    {
        if (instance.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (instance.TryGetInt64(out _))
        {
            return true;
        }

        return instance.TryGetDecimal(out var value) && value == decimal.Truncate(value);
    }

    private static bool HasDuplicates(JsonElement array)
    {
        var items = array.EnumerateArray().ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            for (var j = i + 1; j < items.Length; j++)
            {
                if (JsonValueEquality.Equals(items[i], items[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetInteger(JsonElement schema, string keyword, out long value)
    {
        value = 0;
        return schema.TryGetProperty(keyword, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt64(out value);
    }

    private static bool TryGetNumber(JsonElement schema, string keyword, out decimal value)
    {
        value = 0;
        return schema.TryGetProperty(keyword, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetDecimal(out value);
    }

    private static string Truncate(JsonElement element)
    {
        var raw = element.GetRawText();
        return raw.Length <= 80 ? raw : raw[..77] + "...";
    }
}
