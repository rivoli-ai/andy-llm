using System.Text.Json;
using System.Text.RegularExpressions;

namespace Andy.Llm.Parsing;

/// <summary>
/// Best-effort repair of malformed tool-call argument JSON emitted by weaker / less strict
/// models. Models frequently wrap arguments in markdown fences, add prose, use trailing
/// commas, single quotes, unquoted keys, Python literals (True/False/None), smart quotes, or
/// double-encode the object as a JSON string.
///
/// The strategy is conservative: candidates are generated from least-invasive to most-invasive,
/// each validated by <see cref="JsonDocument"/>, and the first candidate that parses to a JSON
/// object wins. Valid JSON is therefore always returned unchanged.
/// </summary>
public static partial class ToolArgumentJsonRepair
{
    /// <summary>
    /// Attempts to repair <paramref name="raw"/> into a valid JSON object string.
    /// Returns true and sets <paramref name="repairedJson"/> on success.
    /// </summary>
    public static bool TryRepair(string? raw, out string repairedJson)
    {
        repairedJson = "{}";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        foreach (var candidate in Candidates(raw))
        {
            if (TryAsObjectJson(candidate, out var json))
            {
                repairedJson = json;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Best-effort parse of tool-call arguments into a dictionary. Returns an empty dictionary
    /// when the input is blank or unrecoverable.
    /// </summary>
    public static Dictionary<string, object?> ParseArguments(string? raw)
    {
        if (!TryRepair(raw, out var json))
            return new Dictionary<string, object?>();

        using var doc = JsonDocument.Parse(json);
        return ToDictionary(doc.RootElement);
    }

    /// <summary>Parses <paramref name="candidate"/>; if it is a JSON object (or a JSON string
    /// that itself encodes an object), returns its canonical JSON via <paramref name="json"/>.</summary>
    private static bool TryAsObjectJson(string candidate, out string json)
    {
        json = "{}";
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            var root = doc.RootElement;

            // Double-encoded: a JSON string whose content is itself the object.
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                return !string.IsNullOrWhiteSpace(inner) && TryRepair(inner, out json);
            }

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            json = root.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Yields progressively-repaired candidate strings, least-invasive first.</summary>
    private static IEnumerable<string> Candidates(string raw)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool New(string s) => !string.IsNullOrWhiteSpace(s) && seen.Add(s);

        // 1. As-is (so valid JSON is never altered).
        var asIs = raw.Trim();
        if (New(asIs)) yield return asIs;

        // 2. Strip a markdown code fence.
        var fenced = StripCodeFence(asIs);
        if (New(fenced)) yield return fenced;

        // 3. Narrow to the outermost {...} (drops surrounding prose).
        var braced = ExtractOutermostBraces(fenced) ?? fenced;
        if (New(braced)) yield return braced;

        // 4. Progressive structural repairs, applied cumulatively on the braced text.
        var step = braced;

        step = NormalizeSmartQuotes(step);
        if (New(step)) yield return step;

        var singleFixed = SingleToDoubleQuotes(step);
        if (New(singleFixed)) yield return singleFixed;
        step = singleFixed;

        step = QuoteUnquotedKeys(step);
        if (New(step)) yield return step;

        step = NormalizePythonLiterals(step);
        if (New(step)) yield return step;

        step = RemoveTrailingCommas(step);
        if (New(step)) yield return step;
    }

    private static string StripCodeFence(string s)
    {
        if (!s.Contains("```", StringComparison.Ordinal))
            return s;
        var first = s.IndexOf("```", StringComparison.Ordinal);
        var afterOpen = s.IndexOf('\n', first);
        if (afterOpen < 0)
            return s;
        var close = s.IndexOf("```", afterOpen, StringComparison.Ordinal);
        var body = close < 0 ? s[(afterOpen + 1)..] : s[(afterOpen + 1)..close];
        return body.Trim();
    }

    private static string? ExtractOutermostBraces(string s)
    {
        var open = s.IndexOf('{');
        var close = s.LastIndexOf('}');
        return open >= 0 && close > open ? s[open..(close + 1)] : null;
    }

    private static string NormalizeSmartQuotes(string s) => s
        .Replace('“', '"').Replace('”', '"')   // “ ”
        .Replace('‘', '\'').Replace('’', '\''); // ‘ ’

    /// <summary>
    /// Converts a Python/JS-style single-quoted object to double-quoted JSON. Only applied when
    /// the text uses single quotes and contains no double quotes (the common "stringified dict"
    /// case), to avoid corrupting valid JSON that contains apostrophes.
    /// </summary>
    private static string SingleToDoubleQuotes(string s)
    {
        if (s.Contains('"') || !s.Contains('\''))
            return s;
        return s.Replace('\'', '"');
    }

    private static string QuoteUnquotedKeys(string s) =>
        UnquotedKeyRegex().Replace(s, "$1\"$2\"$3");

    private static string NormalizePythonLiterals(string s) =>
        PythonLiteralRegex().Replace(s, m => m.Value switch
        {
            "True" => "true",
            "False" => "false",
            "None" => "null",
            _ => m.Value,
        });

    private static string RemoveTrailingCommas(string s) =>
        TrailingCommaRegex().Replace(s, "$1");

    private static Dictionary<string, object?> ToDictionary(JsonElement obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.EnumerateObject())
            dict[prop.Name] = ToValue(prop.Value);
        return dict;
    }

    private static object? ToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        // Cast to object so the conditional's type is object, not the long/double common type
        // (which would otherwise widen every integer to double).
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => el.GetRawText(),
        JsonValueKind.Array => el.GetRawText(),
        _ => el.GetRawText(),
    };

    // Keys like {name: ...} or , key: ...  ->  quote the bare key.
    [GeneratedRegex(@"([{,]\s*)([A-Za-z_][A-Za-z0-9_]*)(\s*:)")]
    private static partial Regex UnquotedKeyRegex();

    // Python literals only in JSON value position (after : [ or ,, before , } or ]),
    // so the words are not rewritten when they appear inside string content.
    [GeneratedRegex(@"(?<=[:\[,]\s*)(True|False|None)(?=\s*[,}\]])")]
    private static partial Regex PythonLiteralRegex();

    // A comma immediately before a closing } or ].
    [GeneratedRegex(@",(\s*[}\]])")]
    private static partial Regex TrailingCommaRegex();
}
