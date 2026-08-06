using System.Text.Json;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// Structural equality for <see cref="JsonElement"/> values, used for <c>enum</c>/<c>const</c>
/// and <c>uniqueItems</c> checks. Numbers compare by numeric value (<c>1</c> equals <c>1.0</c>),
/// objects compare order-insensitively.
/// </summary>
internal static class JsonValueEquality
{
    public static bool Equals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind == JsonValueKind.Number && b.ValueKind == JsonValueKind.Number)
        {
            if (a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db))
            {
                return da == db;
            }

            return a.GetRawText() == b.GetRawText();
        }

        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        switch (a.ValueKind)
        {
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            case JsonValueKind.Array:
            {
                if (a.GetArrayLength() != b.GetArrayLength())
                {
                    return false;
                }

                using var ea = a.EnumerateArray();
                using var eb = b.EnumerateArray();
                while (ea.MoveNext() && eb.MoveNext())
                {
                    if (!Equals(ea.Current, eb.Current))
                    {
                        return false;
                    }
                }

                return true;
            }
            case JsonValueKind.Object:
            {
                var pa = a.EnumerateObject().ToArray();
                var pb = b.EnumerateObject().ToArray();
                if (pa.Length != pb.Length)
                {
                    return false;
                }

                foreach (var property in pa)
                {
                    if (!b.TryGetProperty(property.Name, out var other) || !Equals(property.Value, other))
                    {
                        return false;
                    }
                }

                return true;
            }
            default:
                // True, False, Null, Undefined: kind equality is sufficient.
                return true;
        }
    }
}
