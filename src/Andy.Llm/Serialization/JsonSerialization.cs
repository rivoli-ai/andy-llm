using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.Llm.Serialization;

/// <summary>
/// JSON serialization helpers: camelCase options and stable stringification for objects of unknown shape.
/// </summary>
public static class JsonSerialization
{
    /// <summary>
    /// Shared camelCase options used when serializing tool data and arguments.
    /// </summary>
    public static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Serialize an object with camelCase property names.
    /// </summary>
    public static string SerializeCamelCase(object value)
    {
        return JsonSerializer.Serialize(value, CamelCaseOptions);
    }

    /// <summary>
    /// Produce a stable JSON string for arbitrary objects by first converting
    /// to a canonical representation with deterministically ordered keys.
    /// </summary>
    public static string StableStringify(object? value)
    {
        var canonical = Canonicalize(value);
        return JsonSerializer.Serialize(canonical, CamelCaseOptions);
    }

    private static object? Canonicalize(object? value)
    {
        if (value is null)
        {
            return null;
        }

        // Handle already-typed dictionaries produced from JSON
        if (value is IDictionary<string, object?> dict)
        {
            var ordered = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kvp in dict)
            {
                ordered[kvp.Key] = Canonicalize(kvp.Value);
            }
            return ordered;
        }

        // Handle arrays
        if (value is IEnumerable<object?> list && value is not string)
        {
            return list.Select(Canonicalize).ToArray();
        }

        // Primitive or unknown CLR types: serialize directly
        return value;
    }
}


