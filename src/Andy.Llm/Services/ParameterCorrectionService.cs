using Andy.Llm.Models;

namespace Andy.Llm.Services;

/// <summary>
/// Suggests minimal corrections for function call arguments based on tool schemas.
/// </summary>
public static class ParameterCorrectionService
{
    /// <summary>
    /// Given a function call and declared tools, propose a corrected argument map (e.g., key remaps).
    /// Returns null if no changes suggested.
    /// </summary>
    public static Dictionary<string, object?>? SuggestCorrections(FunctionCall call, IEnumerable<ToolDeclaration> tools)
    {
        var tool = tools.FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.OrdinalIgnoreCase));
        if (tool == null || call.Arguments == null || call.Arguments.Count == 0)
        {
            return null;
        }

        // Extract expected properties from JSON schema shape: { properties: { ... }, required: [] }
        var expected = ExtractExpectedPropertyNames(tool.Parameters);
        if (expected.Count == 0)
        {
            return null;
        }

        var corrected = new Dictionary<string, object?>(call.Arguments);
        var changed = false;

        foreach (var key in call.Arguments.Keys.ToList())
        {
            if (!expected.Contains(key))
            {
                var candidate = FindNearestKey(key, expected);
                if (candidate != null && !corrected.ContainsKey(candidate))
                {
                    corrected[candidate] = corrected[key];
                    corrected.Remove(key);
                    changed = true;
                }
            }
        }

        return changed ? corrected : null;
    }

    private static HashSet<string> ExtractExpectedPropertyNames(Dictionary<string, object> parameters)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (parameters.TryGetValue("properties", out var propsObj) && propsObj is IDictionary<string, object> props)
        {
            foreach (var name in props.Keys)
            {
                result.Add(name);
            }
        }
        return result;
    }

    private static string? FindNearestKey(string source, HashSet<string> candidates)
    {
        string? best = null;
        var bestDist = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var dist = Levenshtein(source, candidate);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = candidate;
            }
        }
        // Only suggest if reasonably close
        return bestDist <= Math.Max(1, source.Length / 3) ? best : null;
    }

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            dp[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            dp[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }
        return dp[a.Length, b.Length];
    }
}


