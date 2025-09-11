using System.Text.RegularExpressions;

namespace Andy.Llm.Security;

/// <summary>
/// Provides methods to sanitize sensitive data in logs and outputs.
/// </summary>
public static class SensitiveDataSanitizer
{
    private static readonly Regex ApiKeyPattern = new(
        @"(api[_-]?key|apikey|api_secret|access[_-]?token|auth[_-]?token|authorization|token)\s*[:=]\s*['""]?([^'"";\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex GenericKeyPattern = new(
        @"(sk-|key-|token-|bearer\s+)[\w\-]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UrlCredentialsPattern = new(
        @"(https?://)([^:]+):([^@]+)@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes sensitive data in a string by replacing it with masked values.
    /// </summary>
    /// <param name="input">The input string to sanitize.</param>
    /// <returns>The sanitized string.</returns>
    public static string Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var result = input;

        // Sanitize API keys in key-value pairs
        result = ApiKeyPattern.Replace(result, m =>
        {
            var prefix = m.Groups[1].Value;
            return $"{prefix}=***REDACTED***";
        });

        // Sanitize generic key patterns
        result = GenericKeyPattern.Replace(result, m =>
        {
            var prefix = m.Value.Substring(0, Math.Min(4, m.Value.Length));
            return $"{prefix}***REDACTED***";
        });

        // Sanitize credentials in URLs
        result = UrlCredentialsPattern.Replace(result, "${1}***:***@");

        return result;
    }

    /// <summary>
    /// Masks an API key for display purposes, showing only first and last few characters.
    /// </summary>
    /// <param name="apiKey">The API key to mask.</param>
    /// <param name="visibleChars">Number of characters to show at start and end.</param>
    /// <returns>The masked API key.</returns>
    public static string MaskApiKey(string? apiKey, int visibleChars = 4)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return "***EMPTY***";
        }

        if (apiKey.Length <= visibleChars * 2)
        {
            return "***REDACTED***";
        }

        var start = apiKey.Substring(0, visibleChars);
        var end = apiKey.Substring(apiKey.Length - visibleChars);
        var maskedLength = apiKey.Length - (visibleChars * 2);
        var masked = new string('*', Math.Min(maskedLength, 8));

        return $"{start}{masked}{end}";
    }

    /// <summary>
    /// Checks if a string potentially contains sensitive data.
    /// </summary>
    /// <param name="input">The input string to check.</param>
    /// <returns>True if the string may contain sensitive data.</returns>
    public static bool ContainsSensitiveData(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        return ApiKeyPattern.IsMatch(input) ||
               GenericKeyPattern.IsMatch(input) ||
               UrlCredentialsPattern.IsMatch(input);
    }

    /// <summary>
    /// Sanitizes an exception message and stack trace.
    /// </summary>
    /// <param name="exception">The exception to sanitize.</param>
    /// <returns>A sanitized exception message.</returns>
    public static string SanitizeException(Exception? exception)
    {
        if (exception == null)
        {
            return string.Empty;
        }

        var message = Sanitize(exception.Message);
        var stackTrace = exception.StackTrace != null ? Sanitize(exception.StackTrace) : string.Empty;

        var result = $"{exception.GetType().Name}: {message}";

        if (!string.IsNullOrEmpty(stackTrace))
        {
            result += $"\n{stackTrace}";
        }

        if (exception.InnerException != null)
        {
            result += $"\n\nInner Exception:\n{SanitizeException(exception.InnerException)}";
        }

        return result;
    }
}
