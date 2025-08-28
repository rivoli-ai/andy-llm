using Andy.Llm;
using Andy.Llm.Extensions;
using Andy.Llm.Models;
using Andy.Llm.Progress;
using Andy.Llm.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public static async Task Main(string[] args)
    {
        // Setup dependency injection with telemetry
        var services = new ServiceCollection();
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

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

        var serviceProvider = services.BuildServiceProvider();

        // Example 1: Using TelemetryMiddleware
        await RunWithTelemetryMiddleware(serviceProvider);

        // Example 2: Direct metrics recording
        await RunWithDirectMetrics(serviceProvider);

        // Example 3: Custom metrics listener
        await RunWithCustomMetricsListener();

        // Example 4: Progress reporting
        await RunWithProgressReporting();

        Console.WriteLine("\nTelemetry examples completed!");
    }

    static async Task RunWithTelemetryMiddleware(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=== Example 1: Using TelemetryMiddleware ===");
        
        var llmClient = serviceProvider.GetRequiredService<LlmClient>();
        var metrics = serviceProvider.GetRequiredService<LlmMetrics>();
        var logger = serviceProvider.GetRequiredService<ILogger<TelemetryMiddleware>>();
        var telemetry = new TelemetryMiddleware(metrics, logger);

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
                            Message.CreateText(MessageRole.User, "Write a one-line story about telemetry")
                        },
                        MaxTokens = 50
                    };

                    var result = await llmClient.CompleteAsync(request, ct);
                    return result;
                },
                CancellationToken.None);

            Console.WriteLine($"Response: {response.Content}");
            
            if (response.Usage != null)
            {
                Console.WriteLine($"Tokens used - Prompt: {response.Usage.PromptTokens}, Completion: {response.Usage.CompletionTokens}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static async Task RunWithDirectMetrics(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n=== Example 2: Direct Metrics Recording ===");
        
        var metrics = serviceProvider.GetRequiredService<LlmMetrics>();
        var llmClient = serviceProvider.GetRequiredService<LlmClient>();

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

            Console.WriteLine($"Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Operation failed: {ex.Message}");
        }

        // Manual metrics recording
        metrics.RecordRequest("openai", "gpt-4", "chat");
        metrics.RecordLatency("openai", "gpt-4", 150.5, success: true);
        metrics.RecordRetry("openai", 1);
        
        Console.WriteLine("Metrics recorded successfully");
    }

    static async Task RunWithCustomMetricsListener()
    {
        Console.WriteLine("\n=== Example 3: Custom Metrics Listener ===");
        
        var metrics = new LlmMetrics();
        var metricsCollected = new Dictionary<string, double>();

        // Setup custom listener
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == LlmMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
                Console.WriteLine($"Subscribed to instrument: {instrument.Name}");
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagString = string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            Console.WriteLine($"[METRIC] {instrument.Name}: {measurement} [{tagString}]");
            
            // Store for analysis
            metricsCollected[instrument.Name] = metricsCollected.GetValueOrDefault(instrument.Name) + measurement;
        });

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var tagString = string.Join(", ", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            Console.WriteLine($"[METRIC] {instrument.Name}: {measurement:F2} [{tagString}]");
            
            metricsCollected[instrument.Name] = metricsCollected.GetValueOrDefault(instrument.Name) + measurement;
        });

        listener.Start();

        // Generate some metrics
        metrics.RecordRequest("custom", "model-1", "test");
        metrics.RecordLatency("custom", "model-1", 250.75);
        metrics.RecordTokens("custom", "model-1", 100, 50);
        metrics.RecordError("custom", "model-1", "TestError");
        
        await Task.Delay(100); // Let metrics process

        Console.WriteLine("\nCollected metrics summary:");
        foreach (var kvp in metricsCollected)
        {
            Console.WriteLine($"  {kvp.Key}: {kvp.Value:F2}");
        }

        metrics.Dispose();
    }

    static async Task RunWithProgressReporting()
    {
        Console.WriteLine("\n=== Example 4: Progress Reporting ===");
        
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
                Console.WriteLine($"\n[{value.Phase}] {value.Message}");
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