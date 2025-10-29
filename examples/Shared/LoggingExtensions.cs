using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Andy.Llm.Examples.Shared;

/// <summary>
/// Extension methods for configuring clean logging in examples
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Adds clean console logging suitable for examples
    /// </summary>
    public static ILoggingBuilder AddCleanConsole(this ILoggingBuilder builder)
    {
        builder.AddConsole(options =>
        {
            options.FormatterName = "clean";
        })
        .AddConsoleFormatter<CleanConsoleFormatter, CleanConsoleFormatterOptions>(options =>
        {
            options.IncludeTimestamp = false;
            options.UseColors = true;
            options.IncludeException = true;
        });

        // Set default log levels
        builder.SetMinimumLevel(LogLevel.Information);

        // Hide verbose logs from framework components
        builder.AddFilter("System", LogLevel.Warning);
        builder.AddFilter("Microsoft", LogLevel.Warning);
        builder.AddFilter("System.Net.Http", LogLevel.Warning);
        // Allow provider factory logging to show configuration details
        builder.AddFilter("Andy.Llm.Providers.LlmProviderFactory", LogLevel.Information);
        builder.AddFilter("Andy.Llm.Providers", LogLevel.Warning);
        builder.AddFilter("Andy.Llm.Services", LogLevel.Warning);

        return builder;
    }

    /// <summary>
    /// Adds clean console logging with timestamps
    /// </summary>
    public static ILoggingBuilder AddCleanConsoleWithTimestamp(this ILoggingBuilder builder)
    {
        builder.AddConsole(options =>
        {
            options.FormatterName = "clean";
        })
        .AddConsoleFormatter<CleanConsoleFormatter, CleanConsoleFormatterOptions>(options =>
        {
            options.IncludeTimestamp = true;
            options.TimestampFormat = "HH:mm:ss";
            options.UseColors = true;
            options.IncludeException = true;
        });

        // Set default log levels
        builder.SetMinimumLevel(LogLevel.Information);

        // Hide verbose logs from framework components
        builder.AddFilter("System", LogLevel.Warning);
        builder.AddFilter("Microsoft", LogLevel.Warning);
        builder.AddFilter("System.Net.Http", LogLevel.Warning);
        // Allow provider factory logging to show configuration details
        builder.AddFilter("Andy.Llm.Providers.LlmProviderFactory", LogLevel.Information);
        builder.AddFilter("Andy.Llm.Providers", LogLevel.Warning);
        builder.AddFilter("Andy.Llm.Services", LogLevel.Warning);

        return builder;
    }
}
