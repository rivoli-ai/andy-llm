using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Andy.Llm.Models;
using Andy.Llm.Security;

namespace Andy.Llm.Telemetry;

/// <summary>
/// Middleware for adding telemetry to LLM operations.
/// </summary>
public class TelemetryMiddleware
{
    private readonly LlmMetrics _metrics;
    private readonly ILogger<TelemetryMiddleware>? _logger;
    private readonly ActivitySource _activitySource;

    /// <summary>
    /// Initializes a new instance of the TelemetryMiddleware class.
    /// </summary>
    /// <param name="metrics">The metrics collector.</param>
    /// <param name="logger">Optional logger.</param>
    public TelemetryMiddleware(LlmMetrics metrics, ILogger<TelemetryMiddleware>? logger = null)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger;
        _activitySource = new ActivitySource("Andy.Llm.Telemetry", "1.0.0");
    }

    /// <summary>
    /// Wraps an LLM operation with telemetry.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async Task<T> ExecuteWithTelemetryAsync<T>(
        string provider,
        string model,
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(
            $"Llm.{operationName}",
            ActivityKind.Client);

        activity?.SetTag("llm.provider", provider);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.operation", operationName);

        var stopwatch = Stopwatch.StartNew();
        _metrics.IncrementActiveRequests(provider);

        _logger?.LogDebug(
            "Starting LLM operation: {Operation} with {Provider}/{Model}",
            operationName, provider, model);

        try
        {
            _metrics.RecordRequest(provider, model, operationName);
            var result = await operation(cancellationToken);

            var latency = stopwatch.ElapsedMilliseconds;
            _metrics.RecordLatency(provider, model, latency, true);

            activity?.SetTag("llm.latency_ms", latency);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger?.LogInformation(
                "Completed LLM operation: {Operation} in {Latency}ms",
                operationName, latency);

            // Record token usage if available
            if (result is LlmResponse response && response.Usage != null)
            {
                _metrics.RecordTokens(
                    provider,
                    model,
                    response.Usage.PromptTokens,
                    response.Usage.CompletionTokens);

                activity?.SetTag("llm.tokens.prompt", response.Usage.PromptTokens);
                activity?.SetTag("llm.tokens.completion", response.Usage.CompletionTokens);
                activity?.SetTag("llm.tokens.total", response.Usage.TotalTokens);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            var latency = stopwatch.ElapsedMilliseconds;
            _metrics.RecordTimeout(provider, operationName);
            _metrics.RecordLatency(provider, model, latency, false);

            activity?.SetTag("llm.latency_ms", latency);
            activity?.SetStatus(ActivityStatusCode.Error, "Operation cancelled");

            _logger?.LogWarning(
                "LLM operation cancelled: {Operation} after {Latency}ms",
                operationName, latency);

            throw;
        }
        catch (Exception ex)
        {
            var latency = stopwatch.ElapsedMilliseconds;
            var errorType = ex.GetType().Name;

            _metrics.RecordError(provider, model, errorType);
            _metrics.RecordLatency(provider, model, latency, false);

            activity?.SetTag("llm.latency_ms", latency);
            activity?.SetTag("llm.error.type", errorType);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            var sanitizedMessage = SensitiveDataSanitizer.Sanitize(ex.Message);
            _logger?.LogError(
                "LLM operation failed: {Operation} after {Latency}ms. Error: {ErrorType} - {Message}",
                operationName, latency, errorType, sanitizedMessage);

            throw;
        }
        finally
        {
            _metrics.DecrementActiveRequests(provider);
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Wraps a streaming LLM operation with telemetry.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model name.</param>
    /// <param name="operationName">The operation name.</param>
    /// <param name="operation">The streaming operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The wrapped stream.</returns>
    public async IAsyncEnumerable<T> ExecuteStreamingWithTelemetryAsync<T>(
        string provider,
        string model,
        string operationName,
        Func<CancellationToken, IAsyncEnumerable<T>> operation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity(
            $"Llm.{operationName}.Stream",
            ActivityKind.Client);

        activity?.SetTag("llm.provider", provider);
        activity?.SetTag("llm.model", model);
        activity?.SetTag("llm.operation", $"{operationName}.stream");
        activity?.SetTag("llm.streaming", true);

        var stopwatch = Stopwatch.StartNew();
        _metrics.IncrementActiveRequests(provider);

        _logger?.LogDebug(
            "Starting streaming LLM operation: {Operation} with {Provider}/{Model}",
            operationName, provider, model);

        var itemCount = 0;
        var firstItemLatency = 0L;
        Exception? caughtException = null;

        try
        {
            _metrics.RecordRequest(provider, model, $"{operationName}.stream");
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        if (caughtException != null)
        {
            var latency = stopwatch.ElapsedMilliseconds;
            var errorType = caughtException.GetType().Name;

            _metrics.RecordError(provider, model, errorType);
            _metrics.RecordLatency(provider, model, latency, false);

            activity?.SetTag("llm.latency_ms", latency);
            activity?.SetTag("llm.item_count", itemCount);
            activity?.SetTag("llm.error.type", errorType);
            activity?.SetStatus(ActivityStatusCode.Error, caughtException.Message);

            var sanitizedMessage = SensitiveDataSanitizer.Sanitize(caughtException.Message);
            _logger?.LogError(
                "Streaming LLM operation failed: {Operation} after {Latency}ms and {ItemCount} items. Error: {ErrorType} - {Message}",
                operationName, latency, itemCount, errorType, sanitizedMessage);

            _metrics.DecrementActiveRequests(provider);
            stopwatch.Stop();
            throw caughtException;
        }

        var enumerator = operation(cancellationToken).WithCancellation(cancellationToken).GetAsyncEnumerator();

        try
        {
            while (true)
            {
                T item;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        break;
                    }

                    item = enumerator.Current;
                }
                catch (Exception ex)
                {
                    var latency = stopwatch.ElapsedMilliseconds;
                    var errorType = ex.GetType().Name;

                    _metrics.RecordError(provider, model, errorType);
                    _metrics.RecordLatency(provider, model, latency, false);

                    activity?.SetTag("llm.latency_ms", latency);
                    activity?.SetTag("llm.item_count", itemCount);
                    activity?.SetTag("llm.error.type", errorType);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                    var sanitizedMessage = SensitiveDataSanitizer.Sanitize(ex.Message);
                    _logger?.LogError(
                        "Streaming LLM operation failed: {Operation} after {Latency}ms and {ItemCount} items. Error: {ErrorType} - {Message}",
                        operationName, latency, itemCount, errorType, sanitizedMessage);

                    _metrics.DecrementActiveRequests(provider);
                    stopwatch.Stop();
                    throw;
                }

                if (itemCount == 0)
                {
                    firstItemLatency = stopwatch.ElapsedMilliseconds;
                    activity?.SetTag("llm.first_item_latency_ms", firstItemLatency);
                }

                itemCount++;
                yield return item;
            }

            var totalLatency = stopwatch.ElapsedMilliseconds;
            _metrics.RecordLatency(provider, model, totalLatency, true);

            activity?.SetTag("llm.latency_ms", totalLatency);
            activity?.SetTag("llm.item_count", itemCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            _logger?.LogInformation(
                "Completed streaming LLM operation: {Operation} in {Latency}ms with {ItemCount} items",
                operationName, totalLatency, itemCount);
        }
        finally
        {
            await enumerator.DisposeAsync();
            _metrics.DecrementActiveRequests(provider);
            stopwatch.Stop();
        }
    }
}
