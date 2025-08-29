using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.IO;

namespace Andy.Llm.Examples.Shared;

/// <summary>
/// A clean console formatter that removes category names and provides better formatting
/// </summary>
public sealed class CleanConsoleFormatter : ConsoleFormatter, IDisposable
{
    private readonly IDisposable? _optionsReloadToken;
    private CleanConsoleFormatterOptions _formatterOptions;

    public CleanConsoleFormatter(IOptionsMonitor<CleanConsoleFormatterOptions> options)
        : base("clean")
    {
        _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
        _formatterOptions = options.CurrentValue;
    }

    private void ReloadLoggerOptions(CleanConsoleFormatterOptions options)
    {
        _formatterOptions = options;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null)
        {
            return;
        }

        // Write timestamp if configured
        if (_formatterOptions.IncludeTimestamp)
        {
            var timestamp = DateTimeOffset.Now.ToString(_formatterOptions.TimestampFormat);
            WriteColoredMessage(textWriter, timestamp, ConsoleColor.DarkGray);
            textWriter.Write(" ");
        }

        // Write log level with color
        WriteLogLevel(textWriter, logEntry.LogLevel);
        
        // Write the message
        textWriter.WriteLine(message);

        // Write exception if present
        if (logEntry.Exception != null && _formatterOptions.IncludeException)
        {
            // Only show the exception message for cleaner output, unless it's Debug level
            if (logEntry.LogLevel >= LogLevel.Information)
            {
                WriteColoredMessage(textWriter, $"  Details: {logEntry.Exception.Message}", ConsoleColor.DarkRed);
            }
            else
            {
                WriteColoredMessage(textWriter, logEntry.Exception.ToString(), ConsoleColor.Red);
            }
            textWriter.WriteLine();
        }
    }

    private void WriteLogLevel(TextWriter textWriter, LogLevel logLevel)
    {
        string text;
        ConsoleColor? foreground;
        ConsoleColor? background;
        
        switch (logLevel)
        {
            case LogLevel.Trace:
                text = "TRACE";
                foreground = ConsoleColor.Gray;
                background = null;
                break;
            case LogLevel.Debug:
                text = "DEBUG";
                foreground = ConsoleColor.Gray;
                background = null;
                break;
            case LogLevel.Information:
                text = ""; // No prefix for info
                foreground = null;
                background = null;
                break;
            case LogLevel.Warning:
                text = "WARN ";
                foreground = ConsoleColor.Yellow;
                background = null;
                break;
            case LogLevel.Error:
                text = "ERROR";
                foreground = ConsoleColor.White;
                background = ConsoleColor.DarkRed;
                break;
            case LogLevel.Critical:
                text = "CRIT ";
                foreground = ConsoleColor.White;
                background = ConsoleColor.DarkRed;
                break;
            default:
                text = "";
                foreground = null;
                background = null;
                break;
        }

        if (!string.IsNullOrEmpty(text))
        {
            WriteColoredMessage(textWriter, $"[{text}] ", foreground, background);
        }
    }

    private void WriteColoredMessage(
        TextWriter textWriter,
        string message,
        ConsoleColor? foreground,
        ConsoleColor? background = null)
    {
        if (_formatterOptions.UseColors)
        {
            var originalForeground = Console.ForegroundColor;
            var originalBackground = Console.BackgroundColor;

            if (foreground.HasValue)
                Console.ForegroundColor = foreground.Value;
            if (background.HasValue)
                Console.BackgroundColor = background.Value;

            textWriter.Write(message);

            Console.ForegroundColor = originalForeground;
            Console.BackgroundColor = originalBackground;
        }
        else
        {
            textWriter.Write(message);
        }
    }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }
}

public class CleanConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool IncludeTimestamp { get; set; } = false;
    public new string TimestampFormat { get; set; } = "HH:mm:ss";
    public bool IncludeException { get; set; } = true;
    public bool UseColors { get; set; } = true;
}