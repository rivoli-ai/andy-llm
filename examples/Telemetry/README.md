# Telemetry Example

This example demonstrates telemetry, metrics, and monitoring capabilities in the Andy.Llm library.

## What It Does

The Telemetry example showcases four different monitoring scenarios:

1. **Using TelemetryMiddleware**: Automatic metrics collection for LLM requests
2. **Direct Metrics Recording**: Manual recording of custom metrics
3. **Custom Metrics Listener**: Creating custom listeners for metrics events
4. **Progress Reporting**: Real-time progress updates for long-running operations

## Prerequisites

You need an OpenAI API key set as an environment variable:

```bash
export OPENAI_API_KEY=sk-your-api-key-here
```

## Running the Example

From the repository root:

```bash
dotnet run --project examples/Telemetry
```

## Expected Output

When running successfully, you should see:

```
=== Telemetry and Monitoring Examples ===

=== Example 1: Using TelemetryMiddleware ===
Response: [AI response about telemetry]
Tokens used - Prompt: XX, Completion: XX

=== Example 2: Direct Metrics Recording ===
Result: Success
Metrics recorded successfully

=== Example 3: Custom Metrics Listener ===
Subscribed to instrument: llm.request.duration
[METRIC] llm.request.duration: 1234.56 [provider=openai, model=gpt-4o-mini]
[METRIC] llm.token.count: 150 [provider=openai, type=total]

Collected metrics summary:
  llm.request.duration: 1234.56
  llm.token.count: 150.00

=== Example 4: Progress Reporting ===
Processing documents with progress tracking...

[Initialization] Starting document processing...
[Processing] Processing document 1 of 3...
[Processing] Processing document 2 of 3...
[Processing] Processing document 3 of 3...
[Completion] All documents processed successfully!

Telemetry examples completed!
```

## Key Features Demonstrated

### 1. TelemetryMiddleware
- Automatically tracks request duration
- Records token usage
- Captures model and provider information
- Integrates with OpenTelemetry

### 2. Direct Metrics
- Custom counter increments
- Histogram recording for duration tracking
- Gauge values for current state
- Tag-based metric organization

### 3. Custom Metrics Listener
- Subscribe to specific instruments
- Real-time metric event handling
- Aggregation and summary reporting
- Flexible filtering and processing

### 4. Progress Reporting
- IProgress<T> pattern implementation
- Phase-based progress tracking
- Percentage completion
- Status messages

## OpenTelemetry Integration

The example uses OpenTelemetry for metrics collection and export:

```csharp
using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .ConfigureResource(resource => resource.AddService("TelemetryExample"))
    .AddMeter("Andy.Llm")
    .AddConsoleExporter()
    .Build();
```

This enables:
- Structured metrics collection
- Multiple export targets (Console, Prometheus, etc.)
- Distributed tracing support
- Resource attribution

## Metrics Collected

### Automatic Metrics (via TelemetryMiddleware)
- `llm.request.duration` - Time taken for LLM requests (ms)
- `llm.token.count` - Tokens used (prompt, completion, total)
- `llm.request.count` - Number of requests made
- `llm.error.count` - Number of errors encountered

### Custom Metrics
- Any application-specific metrics you define
- Business logic metrics
- Performance counters
- Resource utilization

## Use Cases

- **Performance Monitoring**: Track response times and throughput
- **Cost Management**: Monitor token usage across providers
- **Error Tracking**: Identify and debug failures
- **Capacity Planning**: Understand usage patterns
- **SLA Compliance**: Ensure performance targets are met

## Troubleshooting

If you see no output or errors:
1. Check that your OPENAI_API_KEY is set correctly
2. Ensure you have internet connectivity
3. Verify the API key has sufficient credits
4. Check the console for error messages

The example integrates with standard observability tools and can export metrics to various backends for production monitoring.