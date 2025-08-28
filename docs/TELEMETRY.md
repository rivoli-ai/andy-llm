# Telemetry and Observability Guide

This guide explains how to access and use the telemetry features in Andy.Llm for monitoring, debugging, and performance analysis.

## Overview

Andy.Llm provides comprehensive telemetry through:
- **Metrics**: Counters, histograms, and gauges for operational insights
- **Distributed Tracing**: Request flow tracking across services
- **Progress Reporting**: Real-time operation progress updates
- **Structured Logging**: Contextual logging with automatic sensitive data sanitization

## Architecture

The telemetry system is built on .NET's `System.Diagnostics` APIs, making it compatible with OpenTelemetry and other monitoring solutions.

### Core Components

1. **LlmMetrics**: Collects operational metrics
2. **TelemetryMiddleware**: Wraps operations with telemetry
3. **LlmProgressReporter**: Reports operation progress
4. **ActivitySource**: Provides distributed tracing

## Metrics Collected

### Available Metrics

| Metric Name | Type | Description | Tags |
|------------|------|-------------|------|
| `llm.requests` | Counter | Total number of LLM requests | provider, model, operation |
| `llm.tokens` | Counter | Token usage | provider, model, type (prompt/completion) |
| `llm.request.duration` | Histogram | Request latency in milliseconds | provider, model, success |
| `llm.errors` | Counter | Error counts | provider, model, error_type |
| `llm.active_requests` | UpDownCounter | Currently active requests | provider |
| `llm.retries` | Counter | Retry attempts | provider, attempt |
| `llm.timeouts` | Counter | Request timeouts | provider, operation |

## Integration Options

### Option 1: OpenTelemetry Integration (Recommended)

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "my-app")
        .AddAttributes(new Dictionary<string, object>
        {
            ["environment"] = "production",
            ["version"] = "1.0.0"
        }))
    .WithMetrics(metrics => metrics
        .AddMeter("Andy.Llm")              // Subscribe to Andy.Llm metrics
        .AddPrometheusExporter()            // Export to Prometheus
        .AddOtlpExporter()                  // Export to OTLP collector
        .AddConsoleExporter())              // Console for debugging
    .WithTracing(tracing => tracing
        .AddSource("Andy.Llm.Telemetry")   // Subscribe to traces
        .AddJaegerExporter()                // Export to Jaeger
        .AddZipkinExporter()                // Export to Zipkin
        .AddConsoleExporter());             // Console for debugging

// Add Andy.Llm with telemetry
builder.Services.AddSingleton<LlmMetrics>();
builder.Services.AddSingleton<TelemetryMiddleware>();
```

### Option 2: Direct Metrics Access

```csharp
// Create metrics collector
var metrics = new LlmMetrics();

// Use with operations
var result = await metrics.RecordOperationAsync(
    provider: "openai",
    model: "gpt-4",
    operation: async () => await llmClient.CompleteAsync(request)
);

// Or record manually
metrics.RecordRequest("openai", "gpt-4", "complete");
metrics.RecordLatency("openai", "gpt-4", latencyMs: 250, success: true);
metrics.RecordTokens("openai", "gpt-4", promptTokens: 100, completionTokens: 50);
metrics.RecordError("openai", "gpt-4", "RateLimitExceeded");
```

### Option 3: Custom Metrics Listeners

```csharp
using System.Diagnostics.Metrics;

public class CustomMetricsCollector
{
    private readonly MeterListener _listener;
    private readonly Dictionary<string, long> _metrics = new();

    public CustomMetricsCollector()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Andy.Llm")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var tagString = string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}"));
            var key = $"{instrument.Name}[{tagString}]";
            
            _metrics[key] = _metrics.GetValueOrDefault(key) + measurement;
            
            // Send to your monitoring system
            SendToMonitoring(instrument.Name, measurement, tags);
        });

        _listener.Start();
    }

    private void SendToMonitoring(string name, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        // Your custom monitoring implementation
        Console.WriteLine($"[METRIC] {name}: {value}");
    }
}
```

### Option 4: Application Insights Integration

```csharp
// Install: Microsoft.ApplicationInsights.AspNetCore
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = "InstrumentationKey=...";
});

// Metrics automatically flow to Application Insights
builder.Services.AddSingleton<LlmMetrics>();
builder.Services.AddSingleton<TelemetryMiddleware>();

// Custom events
var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();
telemetryClient.TrackEvent("LlmRequestCompleted", 
    properties: new Dictionary<string, string>
    {
        ["Provider"] = "openai",
        ["Model"] = "gpt-4"
    },
    metrics: new Dictionary<string, double>
    {
        ["Duration"] = 250,
        ["Tokens"] = 150
    });
```

### Option 5: Terminal Application Integration

```csharp
// Simple console telemetry for terminal apps
public class TerminalTelemetryReporter : IProgress<LlmOperationProgress>
{
    private int _lastPercentage = -1;

    public void Report(LlmOperationProgress value)
    {
        if (value.PercentComplete != _lastPercentage)
        {
            Console.Write($"\r[{value.PercentComplete}%] {value.Phase}: {value.Message}");
            _lastPercentage = value.PercentComplete;
        }

        if (value.PercentComplete == 100 || value.Phase == "Error")
        {
            Console.WriteLine();
        }
    }
}

// Usage in terminal app
var progressReporter = new TerminalTelemetryReporter();
var progress = new LlmProgressReporter(progressReporter.Report);

progress.ReportStart("Chat Completion");
progress.ReportPhase("Connecting", 10);
progress.ReportPhase("Sending request", 25);
progress.ReportTokens(50, 200);  // 50 of estimated 200 tokens
progress.ReportPhase("Processing", 75);
progress.ReportCompletion("Response received");
```

## Using TelemetryMiddleware

The `TelemetryMiddleware` automatically adds telemetry to your LLM operations:

```csharp
public class LlmService
{
    private readonly LlmClient _client;
    private readonly TelemetryMiddleware _telemetry;
    
    public LlmService(LlmClient client, LlmMetrics metrics, ILogger<LlmService> logger)
    {
        _client = client;
        _telemetry = new TelemetryMiddleware(metrics, logger);
    }
    
    public async Task<string> GetResponseAsync(string prompt, CancellationToken ct = default)
    {
        return await _telemetry.ExecuteWithTelemetryAsync(
            provider: "openai",
            model: "gpt-4",
            operationName: "GetResponse",
            operation: async (token) =>
            {
                var response = await _client.CompleteAsync(
                    new LlmRequest { Messages = [Message.CreateText(MessageRole.User, prompt)] },
                    token);
                return response.Content;
            },
            ct);
    }
    
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string prompt, 
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in _telemetry.ExecuteStreamingWithTelemetryAsync(
            provider: "openai",
            model: "gpt-4",
            operationName: "StreamResponse",
            operation: (token) => StreamInternalAsync(prompt, token),
            ct))
        {
            yield return chunk;
        }
    }
}
```

## Distributed Tracing

Andy.Llm creates trace spans for each operation:

```csharp
// Spans are automatically created with these attributes:
- llm.provider: The provider name
- llm.model: The model used
- llm.operation: The operation type
- llm.latency_ms: Operation duration
- llm.tokens.prompt: Input token count
- llm.tokens.completion: Output token count
- llm.error.type: Error type (if failed)
```

### Viewing Traces

#### Jaeger
```yaml
# docker-compose.yml
services:
  jaeger:
    image: jaegertracing/all-in-one:latest
    ports:
      - "16686:16686"
      - "6831:6831/udp"
```

Access UI at: http://localhost:16686

#### Zipkin
```yaml
services:
  zipkin:
    image: openzipkin/zipkin
    ports:
      - "9411:9411"
```

Access UI at: http://localhost:9411

## Prometheus + Grafana Setup

### Prometheus Configuration

```yaml
# prometheus.yml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'andy-llm-app'
    static_configs:
      - targets: ['localhost:5000']
    metrics_path: '/metrics'
```

### Grafana Dashboard

Import this dashboard JSON for Andy.Llm metrics:

```json
{
  "dashboard": {
    "title": "Andy.Llm Metrics",
    "panels": [
      {
        "title": "Request Rate",
        "targets": [{
          "expr": "rate(llm_requests_total[5m])",
          "legendFormat": "{{provider}} - {{model}}"
        }]
      },
      {
        "title": "Request Duration",
        "targets": [{
          "expr": "histogram_quantile(0.95, llm_request_duration_bucket)",
          "legendFormat": "p95 - {{provider}}"
        }]
      },
      {
        "title": "Token Usage",
        "targets": [{
          "expr": "rate(llm_tokens_total[5m])",
          "legendFormat": "{{type}} - {{model}}"
        }]
      },
      {
        "title": "Error Rate",
        "targets": [{
          "expr": "rate(llm_errors_total[5m])",
          "legendFormat": "{{error_type}} - {{provider}}"
        }]
      }
    ]
  }
}
```

## Progress Reporting

For long-running operations, use progress reporting:

```csharp
public async Task ProcessDocumentsAsync(
    List<string> documents,
    IProgress<LlmOperationProgress> progress)
{
    var reporter = new LlmProgressReporter(progress.Report);
    
    reporter.ReportStart("Document Processing");
    
    for (int i = 0; i < documents.Count; i++)
    {
        var percentComplete = (i * 100) / documents.Count;
        reporter.ReportPhase(
            $"Processing document {i + 1}/{documents.Count}",
            percentComplete,
            documents[i]);
        
        // Process document
        await ProcessSingleDocumentAsync(documents[i]);
        
        reporter.ReportTokens(
            tokensProcessed: i * 1000,
            estimatedTotal: documents.Count * 1000);
    }
    
    reporter.ReportCompletion($"Processed {documents.Count} documents");
}
```

## Security Considerations

### Sensitive Data Sanitization

All telemetry automatically sanitizes sensitive data:

```csharp
// API keys are automatically redacted
var sanitized = SensitiveDataSanitizer.Sanitize(
    "Using api_key=sk-abc123def456");
// Result: "Using api_key=***REDACTED***"

// Exception sanitization
try
{
    // operation
}
catch (Exception ex)
{
    var safeMessage = SensitiveDataSanitizer.SanitizeException(ex);
    logger.LogError(safeMessage);
}
```

### Secure API Key Storage

Use `SecureApiKeyProvider` for secure key management:

```csharp
var keyProvider = new SecureApiKeyProvider();
keyProvider.SetApiKey("openai", apiKey);  // Stored as SecureString

// Keys are encrypted in memory
var key = keyProvider.GetApiKey("openai");
```

## Best Practices

1. **Use structured logging**: Include correlation IDs for request tracing
2. **Set appropriate sampling**: For high-volume services, use trace sampling
3. **Monitor key metrics**: Focus on latency, error rate, and token usage
4. **Set up alerts**: Configure alerts for error spikes or latency degradation
5. **Use dashboards**: Create dashboards for different stakeholder needs
6. **Implement SLOs**: Define and monitor Service Level Objectives
7. **Regular cleanup**: Archive old telemetry data to manage storage

## Troubleshooting

### No Metrics Appearing

```csharp
// Verify meter is registered
var meters = MeterListener.GetMeters();
var andyMeter = meters.FirstOrDefault(m => m.Name == "Andy.Llm");
if (andyMeter == null)
{
    // Meter not created - ensure LlmMetrics is instantiated
}
```

### Missing Traces

```csharp
// Check activity source
Activity.Current?.Source.Name; // Should be "Andy.Llm.Telemetry"

// Enable verbose logging
builder.Logging.AddFilter("Andy.Llm", LogLevel.Trace);
```

### Performance Impact

```csharp
// Disable telemetry in performance-critical paths
services.Configure<ResilienceOptions>(options =>
{
    options.EnableRetry = false;
    options.EnableCircuitBreaker = false;
    options.EnableTimeout = false;
});
```

## Examples

See the [examples](../examples/) directory for complete telemetry examples:
- [TelemetryExample.cs](../examples/TelemetryExample.cs) - Basic telemetry setup
- [MetricsExporter.cs](../examples/MetricsExporter.cs) - Custom metrics export
- [DistributedTracing.cs](../examples/DistributedTracing.cs) - Trace correlation