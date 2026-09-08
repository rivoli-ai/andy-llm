using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Andy.Llm.Errors;

/// <summary>Portable provider failure details, without the provider's raw response envelope.</summary>
public sealed record LlmProviderError(string Provider, int? StatusCode, double? RetryAfterSeconds, string Message);

/// <summary>A provider failure that retains HTTP metadata across agent boundaries.</summary>
public sealed class LlmProviderException : InvalidOperationException
{
    /// <summary>Structured details suitable for callers and user-facing rendering.</summary>
    public LlmProviderError Error { get; }
    /// <summary>Creates a provider exception with the original failure retained for logging.</summary>
    public LlmProviderException(LlmProviderError error, Exception? innerException = null)
        : base($"{error.Provider}{(error.StatusCode is { } code ? $" (HTTP {code})" : "")}: {error.Message}", innerException)
        => Error = error;

    /// <summary>Captures status and retry headers while the response is available.</summary>
    public static LlmProviderException FromHttpResponse(string provider, HttpResponseMessage response, string body)
    {
        var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds;
        if (retry == null && response.Headers.RetryAfter?.Date is { } date)
        {
            retry = Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds);
        }

        return new(Parse(provider, body, (int)response.StatusCode, retry));
    }

    /// <summary>Normalizes SDK and network exceptions without losing known HTTP metadata.</summary>
    public static LlmProviderException FromException(string provider, Exception exception)
    {
        if (exception is LlmProviderException known)
        {
            return known;
        }

        int? status = (exception as HttpRequestException)?.StatusCode is { } http ? (int)http : null;
        string? retry = null;
        if (exception is System.ClientModel.ClientResultException client)
        {
            status = client.Status;
            client.GetRawResponse()?.Headers.TryGetValue("Retry-After", out retry);
        }
        else if (exception is Azure.RequestFailedException azure)
        {
            status = azure.Status;
            azure.GetRawResponse()?.Headers.TryGetValue("Retry-After", out retry);
        }
        return new(Parse(provider, exception.Message, status, ParseRetry(retry)), exception);
    }

    /// <summary>Normalizes provider error packets that have no HTTP response object.</summary>
    public static LlmProviderException FromMessage(string provider, string message) => new(Parse(provider, message));

    private static LlmProviderError Parse(string provider, string raw, int? status = null, double? retry = null)
    {
        var message = raw;
        var start = raw.IndexOf('{');
        if (start >= 0)
        {
            // Never display the raw JSON envelope: it can contain account IDs,
            // request headers, or an unbounded upstream payload.
            message = "Provider request failed.";
            try
            {
                if (raw.Length - start <= 65536)
                {
                    using var document = JsonDocument.Parse(raw[start..]);
                    var root = document.RootElement;
                    var error = Property(root, "error") ?? root;
                    message = String(error, "message") ?? (error.ValueKind == JsonValueKind.String ? error.GetString() : null) ?? message;
                    status ??= Integer(error, "code");
                    if (Property(error, "metadata") is { } metadata)
                    {
                        provider = String(metadata, "provider_name") ?? provider;
                        retry ??= ParseRetry(String(metadata, "retry_after_seconds"));
                        if (Property(metadata, "headers") is { } headers)
                        {
                            retry ??= ParseRetry(String(headers, "Retry-After"));
                        }

                        if (String(metadata, "raw") is { } detail && !detail.TrimStart().StartsWith('{'))
                        {
                            message = detail;
                        }
                    }
                }
            }
            catch (JsonException) { }
        }
        return new(Clean(provider, 120), status is >= 100 and <= 599 ? status : null,
            retry is >= 0 && double.IsFinite(retry.Value) ? retry : null, Clean(message, 2000));
    }

    private static JsonElement? Property(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        static string Key(string text) => text.Replace("_", "").Replace("-", "").ToLowerInvariant();
        foreach (var property in value.EnumerateObject())
        {
            if (Key(property.Name) == Key(name))
            {
                return property.Value;
            }
        }

        return null;
    }
    private static string? String(JsonElement value, string name) => Property(value, name) is { } field
        && field.ValueKind is JsonValueKind.String or JsonValueKind.Number ? field.ToString() : null;
    private static int? Integer(JsonElement value, string name) => int.TryParse(String(value, name), out var result) ? result : null;
    private static double? ParseRetry(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0)
        {
            return seconds;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds);
        }

        return null;
    }
    private static string Clean(string text, int limit)
    {
        text = Regex.Replace(text, @"\bsk-[A-Za-z0-9_-]{8,}\b|(?i:Bearer\s+)[A-Za-z0-9._~+/=-]+", "[REDACTED]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        text = string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
        return text.Length <= limit ? text : text[..limit] + "...";
    }
}
