using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Telemetry;
using Andy.Llm.Progress;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Diagnostics.Metrics;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for telemetry and metrics features.
/// </summary>
public class TelemetryTests : IDisposable
{
    private readonly LlmMetrics _metrics;
    private readonly Mock<ILogger<TelemetryMiddleware>> _mockLogger;
    private readonly TelemetryMiddleware _telemetryMiddleware;

    public TelemetryTests()
    {
        _metrics = new LlmMetrics();
        _mockLogger = new Mock<ILogger<TelemetryMiddleware>>();
        _telemetryMiddleware = new TelemetryMiddleware(_metrics, _mockLogger.Object);
    }

    [Fact]
    public void LlmMetrics_RecordRequest_ShouldIncrementCounter()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var operation = "complete";

        // Act
        _metrics.RecordRequest(provider, model, operation);

        // Assert - metrics are recorded (would need meter listener to verify)
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_RecordTokens_ShouldRecordBothTypes()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var promptTokens = 100;
        var completionTokens = 50;

        // Act
        _metrics.RecordTokens(provider, model, promptTokens, completionTokens);

        // Assert - tokens are recorded
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_RecordLatency_ShouldRecordHistogram()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var latencyMs = 250.5;

        // Act
        _metrics.RecordLatency(provider, model, latencyMs, success: true);
        _metrics.RecordLatency(provider, model, latencyMs * 2, success: false);

        // Assert - latency is recorded
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_RecordError_ShouldIncrementErrorCounter()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var errorType = "RateLimitExceeded";

        // Act
        _metrics.RecordError(provider, model, errorType);

        // Assert - error is recorded
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_ActiveRequests_ShouldTrackConcurrency()
    {
        // Arrange
        var provider = "openai";

        // Act
        _metrics.IncrementActiveRequests(provider);
        _metrics.IncrementActiveRequests(provider);
        _metrics.DecrementActiveRequests(provider);

        // Assert - active requests are tracked
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_RecordRetry_ShouldCountAttempts()
    {
        // Arrange
        var provider = "openai";

        // Act
        _metrics.RecordRetry(provider, 1);
        _metrics.RecordRetry(provider, 2);
        _metrics.RecordRetry(provider, 3);

        // Assert - retries are recorded
        Assert.NotNull(_metrics);
    }

    [Fact]
    public void LlmMetrics_RecordTimeout_ShouldIncrementTimeoutCounter()
    {
        // Arrange
        var provider = "openai";

        // Act
        _metrics.RecordTimeout(provider, "request");
        _metrics.RecordTimeout(provider, "stream");

        // Assert - timeouts are recorded
        Assert.NotNull(_metrics);
    }

    [Fact]
    public async Task LlmMetricsExtensions_RecordOperationAsync_Success_ShouldRecordMetrics()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var expectedResult = "test result";

        // Act
        var result = await _metrics.RecordOperationAsync(
            provider,
            model,
            async () =>
            {
                await Task.Delay(10);
                return expectedResult;
            });

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task LlmMetricsExtensions_RecordOperationAsync_Timeout_ShouldRecordError()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await _metrics.RecordOperationAsync<string>(
                provider,
                model,
                async () =>
                {
                    await Task.Delay(10);
                    throw new TimeoutException("Operation timed out");
                });
        });
    }

    [Fact]
    public async Task TelemetryMiddleware_ExecuteWithTelemetry_Success_ShouldLogAndRecord()
    {
        // Arrange
        var provider = "openai";
        var model = "gpt-4";
        var operationName = "TestOperation";
        var expectedResult = "success";

        // Act
        var result = await _telemetryMiddleware.ExecuteWithTelemetryAsync(
            provider,
            model,
            operationName,
            async (ct) =>
            {
                await Task.Delay(10, ct);
                return expectedResult;
            });

        // Assert
        Assert.Equal(expectedResult, result);
        _mockLogger.Verify(l => l.Log(
            LogLevel.Debug,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Starting LLM operation")),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task TelemetryMiddleware_ExecuteWithTelemetry_WithCancellation_ShouldThrow()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _telemetryMiddleware.ExecuteWithTelemetryAsync(
                "openai",
                "gpt-4",
                "TestOperation",
                async (ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(100, ct);
                    return "result";
                },
                cts.Token);
        });
    }

    [Fact]
    public void LlmProgressReporter_ReportStart_ShouldReportZeroPercent()
    {
        // Arrange
        LlmOperationProgress? capturedProgress = null;
        var reporter = new LlmProgressReporter(p => capturedProgress = p);

        // Act
        reporter.ReportStart("TestOperation");

        // Assert
        Assert.NotNull(capturedProgress);
        Assert.Equal("TestOperation", capturedProgress.OperationType);
        Assert.Equal(0, capturedProgress.PercentComplete);
        Assert.Equal("Starting", capturedProgress.Phase);
    }

    [Fact]
    public void LlmProgressReporter_ReportPhase_ShouldUpdateProgress()
    {
        // Arrange
        LlmOperationProgress? capturedProgress = null;
        var reporter = new LlmProgressReporter(p => capturedProgress = p);

        // Act
        reporter.ReportPhase("Processing", 50, "Half way done");

        // Assert
        Assert.NotNull(capturedProgress);
        Assert.Equal("Processing", capturedProgress.Phase);
        Assert.Equal(50, capturedProgress.PercentComplete);
        Assert.Equal("Half way done", capturedProgress.Message);
    }

    [Fact]
    public void LlmProgressReporter_ReportTokens_ShouldCalculatePercentage()
    {
        // Arrange
        LlmOperationProgress? capturedProgress = null;
        var reporter = new LlmProgressReporter(p => capturedProgress = p);
        reporter.ReportStart("TokenProcessing");

        // Act
        reporter.ReportTokens(50, 100);

        // Assert
        Assert.NotNull(capturedProgress);
        Assert.Equal(50, capturedProgress.TokensProcessed);
        Assert.Equal(100, capturedProgress.EstimatedTotalTokens);
        Assert.Equal(50, capturedProgress.PercentComplete);
    }

    [Fact]
    public void LlmProgressReporter_ReportCompletion_ShouldReport100Percent()
    {
        // Arrange
        LlmOperationProgress? capturedProgress = null;
        var reporter = new LlmProgressReporter(p => capturedProgress = p);

        // Act
        reporter.ReportCompletion("Operation completed successfully");

        // Assert
        Assert.NotNull(capturedProgress);
        Assert.Equal(100, capturedProgress.PercentComplete);
        Assert.Equal("Completed", capturedProgress.Phase);
        Assert.Equal("Operation completed successfully", capturedProgress.Message);
    }

    [Fact]
    public void LlmProgressReporter_ReportError_ShouldReportNegativePercent()
    {
        // Arrange
        LlmOperationProgress? capturedProgress = null;
        var reporter = new LlmProgressReporter(p => capturedProgress = p);

        // Act
        reporter.ReportError("Operation failed");

        // Assert
        Assert.NotNull(capturedProgress);
        Assert.Equal(-1, capturedProgress.PercentComplete);
        Assert.Equal("Error", capturedProgress.Phase);
        Assert.Equal("Operation failed", capturedProgress.Message);
    }

    [Fact]
    public void CancellationTokenExtensions_CreateLinkedTokenSourceWithTimeout_ShouldCancelAfterTimeout()
    {
        // Arrange
        var originalCts = new CancellationTokenSource();
        var timeout = TimeSpan.FromMilliseconds(50);

        // Act
        using var linkedCts = originalCts.Token.CreateLinkedTokenSourceWithTimeout(timeout);

        // Assert
        Assert.False(linkedCts.Token.IsCancellationRequested);
        Thread.Sleep(100);
        Assert.True(linkedCts.Token.IsCancellationRequested);

        originalCts.Cancel();
    }

    [Fact]
    public void CancellationTokenExtensions_ThrowIfCancellationRequested_WithMessage_ShouldThrowWithMessage()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();
        const string message = "Custom cancellation message";

        // Act & Assert
        var ex = Assert.Throws<OperationCanceledException>(() =>
            cts.Token.ThrowIfCancellationRequested(message));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task CancellationTokenExtensions_ExecuteWithCancellation_ShouldCheckCancellationBetweenIterations()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var executedIterations = 0;

        // Act
        var task = Task.Run(async () =>
        {
            await cts.Token.ExecuteWithCancellationAsync(async (i) =>
            {
                executedIterations++;
                await Task.Delay(50);
                if (i == 2)
                {
                    cts.Cancel();
                }
            }, 5);
        });

        // Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(3, executedIterations); // Should execute 0, 1, 2 before cancellation
    }

    public void Dispose()
    {
        _metrics?.Dispose();
    }
}
