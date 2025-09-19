using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Andy.Llm.Telemetry;
using Andy.Llm.Progress;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Andy.Llm.Examples.Shared;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics.Metrics;

/// <summary>
/// Example demonstrating telemetry and monitoring with Andy.Llm
/// </summary>
public class TelemetryExample
{
    public static async Task Main()
    {
        // Setup dependency injection with telemetry
        var services = new ServiceCollection();

        // Configure logging with clean console output
        services.AddLogging(builder => builder.AddCleanConsole());

        // Configure OpenTelemetry using separate configuration
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(resource => resource.AddService("TelemetryExample"))
            .AddMeter("Andy.Llm")
            .AddConsoleExporter()
            .Build();

        using var traceProvider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService("TelemetryExample"))
            .AddSource("Andy.Llm.Telemetry")
            .AddConsoleExporter()
            .Build();

        // Add LLM services
        services.AddSingleton<LlmMetrics>();
        services.AddSingleton<TelemetryMiddleware>();
        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
        });

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<TelemetryExample>>();

        try
        {
            // Check for API key
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            {
                logger.LogError("OPENAI_API_KEY environment variable is not set!");
                logger.LogError("Please set your OpenAI API key:");
                logger.LogError("  export OPENAI_API_KEY=sk-...");
                return;
            }

            logger.LogInformation("=== Telemetry and Monitoring Examples ===\n");

            // Example 1: Using TelemetryMiddleware
            await RunWithTelemetryMiddleware(serviceProvider, logger);

            // Example 2: Direct metrics recording
            await RunWithDirectMetrics(serviceProvider, logger);

            // Example 3: Custom metrics listener
            await RunWithCustomMetricsListener(logger);

            // Example 4: Progress reporting
            await RunWithProgressReporting(logger);

            logger.LogInformation("\nTelemetry examples completed!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during telemetry examples");
        }
    }

    static async Task RunWithTelemetryMiddleware(IServiceProvider serviceProvider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 1: Using TelemetryMiddleware ===");

        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
        var llmClient = await factory.CreateAvailableProviderAsync();
        var metrics = serviceProvider.GetRequiredService<LlmMetrics>();
        var telemetryLogger = serviceProvider.GetRequiredService<ILogger<TelemetryMiddleware>>();
        var telemetry = new TelemetryMiddleware(metrics, telemetryLogger);

        try
        {
            var response = await telemetry.ExecuteWithTelemetryAsync(
                provider: "openai",
                model: "gpt-4",
                operationName: "GenerateStory",
                operation: async (ct) =>
                {
                    var request = new LlmRequest
                    {
                        Messages = new List<Message>
                        {
                            new Message { Role = Role.User, Content = "Write a one-line story about telemetry" }
                        },
                        Config = new LlmClientConfig { MaxTokens = 50 }
                    };

                    var result = await llmClient.CompleteAsync(request, ct);
                    return result;
                },
                CancellationToken.None);

            logger.LogInformation("Response: {Response}", response.Content);

            if (response.Usage != null)
            {
                logger.LogInformation("Tokens used - Prompt: {PromptTokens}, Completion: {CompletionTokens}",
                    response.Usage.PromptTokens, response.Usage.CompletionTokens);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error: {Message}", ex.Message);
        }
    }

    static async Task RunWithDirectMetrics(IServiceProvider serviceProvider, ILogger logger)
    {
        logger.LogInformation("\n=== Example 2: Direct Metrics Recording ===");

        var metrics = serviceProvider.GetRequiredService<LlmMetrics>();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
        var llmClient = await factory.CreateAvailableProviderAsync();

        // Record operation with automatic metrics
        try
        {
            var result = await metrics.RecordOperationAsync(
                provider: "openai",
                model: "gpt-4",
                operation: async () =>
                {
                    // Simulate operation
                    await Task.Delay(100);

                    // Record additional metrics
                    metrics.RecordTokens("openai", "gpt-4", promptTokens: 25, completionTokens: 35);

                    return "Operation completed successfully";
                });

            logger.LogInformation("Result: {Result}", result);
        }
        catch (Exception ex)
        {
            logger.LogError("Operation failed: {Message}", ex.Message);
        }

        // Manual metrics recording
        metrics.RecordRequest("openai", "gpt-4", "chat");
        metrics.RecordLatency("openai", "gpt-4", 150.5, success: true);
        metrics.RecordRetry("openai", 1);

        logger.LogInformation("Metrics recorded successfully");
    }

    static async Task RunWithCustomMetricsListener(ILogger logger)
    {
        logger.LogInformation("\n=== Example 3: Custom Metrics Listener ===");

        var metrics = new LlmMetrics();
        var metricsCollected = new Dictionary<string, double>();

        // Setup custom listener
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == LlmMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
                logger.LogInformation("Subscribed to instrument: {InstrumentName}", instrument.Name);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagString = string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            logger.LogInformation("[METRIC] {InstrumentName}: {Measurement} [{Tags}]",
                instrument.Name, measurement, tagString);

            // Store for analysis
            metricsCollected[instrument.Name] = metricsCollected.GetValueOrDefault(instrument.Name) + measurement;
        });

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var tagString = string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            logger.LogInformation("[METRIC] {InstrumentName}: {Measurement:F2} [{Tags}]",
                instrument.Name, measurement, tagString);

            metricsCollected[instrument.Name] = metricsCollected.GetValueOrDefault(instrument.Name) + measurement;
        });

        listener.Start();

        // Generate some metrics
        metrics.RecordRequest("custom", "model-1", "test");
        metrics.RecordLatency("custom", "model-1", 250.75);
        metrics.RecordTokens("custom", "model-1", 100, 50);
        metrics.RecordError("custom", "model-1", "TestError");

        await Task.Delay(100); // Let metrics process

        logger.LogInformation("\nCollected metrics summary:");
        foreach (var kvp in metricsCollected)
        {
            logger.LogInformation("  {Key}: {Value:F2}", kvp.Key, kvp.Value);
        }

        metrics.Dispose();
    }

    static async Task RunWithProgressReporting(ILogger logger)
    {
        logger.LogInformation("\n=== Example 4: Progress Reporting ===");

        var progressReporter = new ConsoleProgressReporter();
        await ProcessDocumentsWithProgress(progressReporter);
    }

    static async Task ProcessDocumentsWithProgress(IProgress<LlmOperationProgress> progress)
    {
        var reporter = new LlmProgressReporter(progress.Report);
        var documents = new[] { "doc1.txt", "doc2.txt", "doc3.txt", "doc4.txt", "doc5.txt" };

        reporter.ReportStart("Document Processing");
        await Task.Delay(500);

        for (int i = 0; i < documents.Length; i++)
        {
            var percentComplete = ((i + 1) * 100) / documents.Length;
            reporter.ReportPhase(
                $"Processing {documents[i]}",
                percentComplete,
                $"Document {i + 1} of {documents.Length}");

            // Simulate processing
            await Task.Delay(300);

            // Report token progress
            reporter.ReportTokens(
                tokensProcessed: (i + 1) * 500,
                estimatedTotal: documents.Length * 500);

            await Task.Delay(200);
        }

        reporter.ReportCompletion($"Successfully processed {documents.Length} documents");
    }

    /// <summary>
    /// Console-based progress reporter for terminal display
    /// </summary>
    class ConsoleProgressReporter : IProgress<LlmOperationProgress>
    {
        private int _lastPercentage = -1;
        private string _lastPhase = "";

        public void Report(LlmOperationProgress value)
        {
            // Clear current line and write progress
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

            if (value.Phase != _lastPhase)
            {
                Console.Write($"\n[{value.Phase}] {value.Message}");
                _lastPhase = value.Phase;
            }

            if (value.PercentComplete >= 0)
            {
                var progressBar = GenerateProgressBar(value.PercentComplete);
                var timeInfo = value.EstimatedTimeRemaining.HasValue
                    ? $" ETA: {value.EstimatedTimeRemaining.Value.TotalSeconds:F1}s"
                    : "";

                Console.Write($"{progressBar} {value.PercentComplete}%{timeInfo}");

                if (value.TokensProcessed > 0)
                {
                    Console.Write($" | Tokens: {value.TokensProcessed}");
                    if (value.EstimatedTotalTokens.HasValue)
                    {
                        Console.Write($"/{value.EstimatedTotalTokens}");
                    }
                }
            }

            if (value.Phase == "Completed" || value.Phase == "Error")
            {
                Console.WriteLine(); // New line after completion
            }

            _lastPercentage = value.PercentComplete;
        }

        private string GenerateProgressBar(int percentage)
        {
            const int barLength = 30;
            var filled = (int)((percentage / 100.0) * barLength);
            var empty = barLength - filled;

            return $"[{new string('=', filled)}{new string('-', empty)}]";
        }
    }
}
