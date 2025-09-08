using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using System.Net;

namespace Andy.Llm.Resilience;

/// <summary>
/// Provides resilience policies for HTTP operations using Polly.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Creates a retry policy for HTTP operations with exponential backoff.
    /// </summary>
    /// <param name="logger">Optional logger for retry attempts.</param>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts (default: 3).</param>
    /// <returns>The retry policy.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        ILogger? logger = null,
        int maxRetryAttempts = 3)
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // Handles HttpRequestException, 5XX and 408
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                maxRetryAttempts,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var reason = outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message ?? "Unknown";
                    logger?.LogWarning(
                        "Retry attempt {RetryCount} after {TimeSpan}ms. Reason: {Reason}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        reason);
                });
    }

    /// <summary>
    /// Creates a circuit breaker policy to prevent overwhelming failing services.
    /// </summary>
    /// <param name="logger">Optional logger for circuit state changes.</param>
    /// <param name="handledEventsAllowedBeforeBreaking">Number of failures before breaking (default: 3).</param>
    /// <param name="durationOfBreak">Duration to keep circuit open (default: 30 seconds).</param>
    /// <returns>The circuit breaker policy.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        ILogger? logger = null,
        int handledEventsAllowedBeforeBreaking = 3,
        TimeSpan? durationOfBreak = null)
    {
        durationOfBreak ??= TimeSpan.FromSeconds(30);

        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking,
                durationOfBreak.Value,
                onBreak: (result, timespan) =>
                {
                    logger?.LogWarning(
                        "Circuit breaker opened for {Duration}s",
                        timespan.TotalSeconds);
                },
                onReset: () =>
                {
                    logger?.LogInformation("Circuit breaker reset");
                },
                onHalfOpen: () =>
                {
                    logger?.LogInformation("Circuit breaker is half-open");
                });
    }

    /// <summary>
    /// Creates a timeout policy for HTTP operations.
    /// </summary>
    /// <param name="timeout">The timeout duration (default: 30 seconds).</param>
    /// <returns>The timeout policy.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        return Policy.TimeoutAsync<HttpResponseMessage>(timeout.Value);
    }

    /// <summary>
    /// Creates a combined resilience policy with retry, circuit breaker, and timeout.
    /// </summary>
    /// <param name="logger">Optional logger for policy events.</param>
    /// <param name="options">Configuration options for the policies.</param>
    /// <returns>The combined policy.</returns>
    public static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy(
        ILogger? logger = null,
        ResilienceOptions? options = null)
    {
        options ??= new ResilienceOptions();

        var retryPolicy = GetRetryPolicy(logger, options.MaxRetryAttempts);
        var circuitBreakerPolicy = GetCircuitBreakerPolicy(
            logger,
            options.CircuitBreakerThreshold,
            options.CircuitBreakerDuration);
        var timeoutPolicy = GetTimeoutPolicy(options.Timeout);

        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy, timeoutPolicy);
    }

    /// <summary>
    /// Creates a simple retry policy for non-HTTP operations.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="logger">Optional logger for retry attempts.</param>
    /// <param name="maxRetryAttempts">Maximum number of retry attempts.</param>
    /// <param name="shouldRetry">Predicate to determine if should retry based on exception.</param>
    /// <returns>The retry policy.</returns>
    public static IAsyncPolicy<T> GetGenericRetryPolicy<T>(
        ILogger? logger = null,
        int maxRetryAttempts = 3,
        Predicate<Exception>? shouldRetry = null)
    {
        var policyBuilder = Policy<T>
            .Handle<Exception>(ex => shouldRetry?.Invoke(ex) ?? true);

        return policyBuilder
            .WaitAndRetryAsync(
                maxRetryAttempts,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var reason = outcome.Exception?.Message ?? "Unknown error";
                    logger?.LogWarning(
                        "Retry attempt {RetryCount} after {TimeSpan}ms. Reason: {Reason}",
                        retryCount,
                        timespan.TotalMilliseconds,
                        reason);
                });
    }
}

/// <summary>
/// Configuration options for resilience policies.
/// </summary>
public class ResilienceOptions
{
    /// <summary>
    /// Maximum number of retry attempts.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Number of failures before circuit breaker opens.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// Duration to keep circuit breaker open.
    /// </summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for operations.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Enable retry policies.
    /// </summary>
    public bool EnableRetry { get; set; } = true;

    /// <summary>
    /// Enable circuit breaker.
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = true;

    /// <summary>
    /// Enable timeout policies.
    /// </summary>
    public bool EnableTimeout { get; set; } = true;
}
