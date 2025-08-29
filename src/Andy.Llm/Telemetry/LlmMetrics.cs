using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Andy.Llm.Telemetry;

/// <summary>
/// Provides metrics collection for LLM operations.
/// </summary>
public class LlmMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Counter<long> _tokenCounter;
    private readonly Histogram<double> _latencyHistogram;
    private readonly Counter<long> _errorCounter;
    private readonly UpDownCounter<long> _activeRequests;
    private readonly Counter<long> _retryCounter;
    private readonly Counter<long> _timeoutCounter;
    private bool _disposed;

    /// <summary>
    /// Gets the name of the metrics meter.
    /// </summary>
    public const string MeterName = "Andy.Llm";

    /// <summary>
    /// Initializes a new instance of the LlmMetrics class.
    /// </summary>
    /// <param name="meterName">Optional custom meter name.</param>
    /// <param name="version">Optional meter version.</param>
    public LlmMetrics(string? meterName = null, string? version = null)
    {
        _meter = new Meter(meterName ?? MeterName, version ?? "1.0.0");

        _requestCounter = _meter.CreateCounter<long>(
            "llm.requests",
            "requests",
            "Total number of LLM requests");

        _tokenCounter = _meter.CreateCounter<long>(
            "llm.tokens",
            "tokens",
            "Total number of tokens processed");

        _latencyHistogram = _meter.CreateHistogram<double>(
            "llm.request.duration",
            "milliseconds",
            "Request latency in milliseconds");

        _errorCounter = _meter.CreateCounter<long>(
            "llm.errors",
            "errors",
            "Total number of errors");

        _activeRequests = _meter.CreateUpDownCounter<long>(
            "llm.active_requests",
            "requests",
            "Number of active requests");

        _retryCounter = _meter.CreateCounter<long>(
            "llm.retries",
            "retries",
            "Total number of retry attempts");

        _timeoutCounter = _meter.CreateCounter<long>(
            "llm.timeouts",
            "timeouts",
            "Total number of request timeouts");
    }

    /// <summary>
    /// Records a request with the specified attributes.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="operation">The operation type.</param>
    public void RecordRequest(string provider, string model, string operation = "complete")
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "operation", operation }
        };

        _requestCounter.Add(1, tags);
    }

    /// <summary>
    /// Records token usage.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="promptTokens">Number of prompt tokens.</param>
    /// <param name="completionTokens">Number of completion tokens.</param>
    public void RecordTokens(string provider, string model, int promptTokens, int completionTokens)
    {
        var promptTags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "type", "prompt" }
        };

        var completionTags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "type", "completion" }
        };

        _tokenCounter.Add(promptTokens, promptTags);
        _tokenCounter.Add(completionTokens, completionTags);
    }

    /// <summary>
    /// Records request latency.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="latencyMs">Latency in milliseconds.</param>
    /// <param name="success">Whether the request was successful.</param>
    public void RecordLatency(string provider, string model, double latencyMs, bool success = true)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "success", success.ToString().ToLower() }
        };

        _latencyHistogram.Record(latencyMs, tags);
    }

    /// <summary>
    /// Records an error.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="errorType">The type of error.</param>
    public void RecordError(string provider, string model, string errorType)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "model", model },
            { "error_type", errorType }
        };

        _errorCounter.Add(1, tags);
    }

    /// <summary>
    /// Increments the active request counter.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    public void IncrementActiveRequests(string provider)
    {
        var tags = new TagList { { "provider", provider } };
        _activeRequests.Add(1, tags);
    }

    /// <summary>
    /// Decrements the active request counter.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    public void DecrementActiveRequests(string provider)
    {
        var tags = new TagList { { "provider", provider } };
        _activeRequests.Add(-1, tags);
    }

    /// <summary>
    /// Records a retry attempt.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="attemptNumber">The retry attempt number.</param>
    public void RecordRetry(string provider, int attemptNumber)
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "attempt", attemptNumber.ToString() }
        };

        _retryCounter.Add(1, tags);
    }

    /// <summary>
    /// Records a timeout.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="operation">The operation that timed out.</param>
    public void RecordTimeout(string provider, string operation = "request")
    {
        var tags = new TagList
        {
            { "provider", provider },
            { "operation", operation }
        };

        _timeoutCounter.Add(1, tags);
    }

    /// <summary>
    /// Creates a new activity for tracing.
    /// </summary>
    /// <param name="name">The activity name.</param>
    /// <param name="kind">The activity kind.</param>
    /// <returns>The created activity or null.</returns>
    public static Activity? StartActivity(
        string name,
        ActivityKind kind = ActivityKind.Client)
    {
        return Activity.Current?.Source.StartActivity(name, kind);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the LlmMetrics and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _meter?.Dispose();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Extension class for easy metrics usage.
/// </summary>
public static class LlmMetricsExtensions
{
    /// <summary>
    /// Records a complete request operation with timing.
    /// </summary>
    /// <param name="metrics">The metrics instance.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="operation">The operation to perform.</param>
    /// <returns>A task representing the operation result.</returns>
    public static async Task<T> RecordOperationAsync<T>(
        this LlmMetrics metrics,
        string provider,
        string model,
        Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        metrics.IncrementActiveRequests(provider);
        
        try
        {
            metrics.RecordRequest(provider, model);
            var result = await operation();
            metrics.RecordLatency(provider, model, stopwatch.ElapsedMilliseconds, true);
            return result;
        }
        catch (TimeoutException)
        {
            metrics.RecordTimeout(provider);
            metrics.RecordError(provider, model, "Timeout");
            metrics.RecordLatency(provider, model, stopwatch.ElapsedMilliseconds, false);
            throw;
        }
        catch (Exception ex)
        {
            metrics.RecordError(provider, model, ex.GetType().Name);
            metrics.RecordLatency(provider, model, stopwatch.ElapsedMilliseconds, false);
            throw;
        }
        finally
        {
            metrics.DecrementActiveRequests(provider);
            stopwatch.Stop();
        }
    }
}