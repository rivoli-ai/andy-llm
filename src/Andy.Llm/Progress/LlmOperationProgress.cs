namespace Andy.Llm.Progress;

/// <summary>
/// Represents progress information for LLM operations.
/// </summary>
public class LlmOperationProgress
{
    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current phase of the operation.
    /// </summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress percentage (0-100).
    /// </summary>
    public int PercentComplete { get; set; }

    /// <summary>
    /// Gets or sets the message describing current progress.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of tokens processed so far.
    /// </summary>
    public int TokensProcessed { get; set; }

    /// <summary>
    /// Gets or sets the estimated total tokens.
    /// </summary>
    public int? EstimatedTotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the elapsed time.
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Gets or sets whether the operation can be cancelled.
    /// </summary>
    public bool CanCancel { get; set; } = true;

    /// <summary>
    /// Gets or sets additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Interface for reporting progress of LLM operations.
/// </summary>
public interface ILlmProgressReporter : IProgress<LlmOperationProgress>
{
    /// <summary>
    /// Reports that an operation has started.
    /// </summary>
    /// <param name="operationType">The type of operation.</param>
    void ReportStart(string operationType);

    /// <summary>
    /// Reports progress with a specific phase.
    /// </summary>
    /// <param name="phase">The current phase.</param>
    /// <param name="percentComplete">The completion percentage.</param>
    /// <param name="message">Optional message.</param>
    void ReportPhase(string phase, int percentComplete, string? message = null);

    /// <summary>
    /// Reports token processing progress.
    /// </summary>
    /// <param name="tokensProcessed">Number of tokens processed.</param>
    /// <param name="estimatedTotal">Estimated total tokens.</param>
    void ReportTokens(int tokensProcessed, int? estimatedTotal = null);

    /// <summary>
    /// Reports that an operation has completed.
    /// </summary>
    /// <param name="message">Optional completion message.</param>
    void ReportCompletion(string? message = null);

    /// <summary>
    /// Reports that an operation has failed.
    /// </summary>
    /// <param name="error">The error message.</param>
    void ReportError(string error);
}

/// <summary>
/// Default implementation of ILlmProgressReporter.
/// </summary>
public class LlmProgressReporter : ILlmProgressReporter
{
    private readonly Action<LlmOperationProgress>? _progressHandler;
    private readonly System.Diagnostics.Stopwatch _stopwatch;
    private string _currentOperation = string.Empty;

    /// <summary>
    /// Initializes a new instance of the LlmProgressReporter class.
    /// </summary>
    /// <param name="progressHandler">Optional progress handler.</param>
    public LlmProgressReporter(Action<LlmOperationProgress>? progressHandler = null)
    {
        _progressHandler = progressHandler;
        _stopwatch = new System.Diagnostics.Stopwatch();
    }

    /// <summary>
    /// Reports progress.
    /// </summary>
    /// <param name="value">The progress value.</param>
    public void Report(LlmOperationProgress value)
    {
        value.ElapsedTime = _stopwatch.Elapsed;
        _progressHandler?.Invoke(value);
    }

    /// <summary>
    /// Reports that an operation has started.
    /// </summary>
    /// <param name="operationType">The type of operation.</param>
    public void ReportStart(string operationType)
    {
        _currentOperation = operationType;
        _stopwatch.Restart();
        
        Report(new LlmOperationProgress
        {
            OperationType = operationType,
            Phase = "Starting",
            PercentComplete = 0,
            Message = $"Starting {operationType}"
        });
    }

    /// <summary>
    /// Reports progress with a specific phase.
    /// </summary>
    /// <param name="phase">The current phase.</param>
    /// <param name="percentComplete">The completion percentage.</param>
    /// <param name="message">Optional message.</param>
    public void ReportPhase(string phase, int percentComplete, string? message = null)
    {
        Report(new LlmOperationProgress
        {
            OperationType = _currentOperation,
            Phase = phase,
            PercentComplete = Math.Min(100, Math.Max(0, percentComplete)),
            Message = message ?? phase
        });
    }

    /// <summary>
    /// Reports token processing progress.
    /// </summary>
    /// <param name="tokensProcessed">Number of tokens processed.</param>
    /// <param name="estimatedTotal">Estimated total tokens.</param>
    public void ReportTokens(int tokensProcessed, int? estimatedTotal = null)
    {
        var percentComplete = estimatedTotal.HasValue && estimatedTotal.Value > 0
            ? (int)((tokensProcessed / (double)estimatedTotal.Value) * 100)
            : -1;

        TimeSpan? estimatedRemaining = null;
        if (percentComplete > 0 && _stopwatch.IsRunning)
        {
            var elapsed = _stopwatch.Elapsed;
            var totalEstimated = elapsed.TotalSeconds / (percentComplete / 100.0);
            estimatedRemaining = TimeSpan.FromSeconds(totalEstimated - elapsed.TotalSeconds);
        }

        Report(new LlmOperationProgress
        {
            OperationType = _currentOperation,
            Phase = "Processing",
            PercentComplete = Math.Max(0, percentComplete),
            Message = $"Processing tokens: {tokensProcessed}{(estimatedTotal.HasValue ? $"/{estimatedTotal}" : "")}",
            TokensProcessed = tokensProcessed,
            EstimatedTotalTokens = estimatedTotal,
            EstimatedTimeRemaining = estimatedRemaining
        });
    }

    /// <summary>
    /// Reports that an operation has completed.
    /// </summary>
    /// <param name="message">Optional completion message.</param>
    public void ReportCompletion(string? message = null)
    {
        _stopwatch.Stop();
        
        Report(new LlmOperationProgress
        {
            OperationType = _currentOperation,
            Phase = "Completed",
            PercentComplete = 100,
            Message = message ?? $"{_currentOperation} completed"
        });
    }

    /// <summary>
    /// Reports that an operation has failed.
    /// </summary>
    /// <param name="error">The error message.</param>
    public void ReportError(string error)
    {
        _stopwatch.Stop();
        
        Report(new LlmOperationProgress
        {
            OperationType = _currentOperation,
            Phase = "Error",
            PercentComplete = -1,
            Message = error
        });
    }
}

/// <summary>
/// Extension methods for cancellation token support.
/// </summary>
public static class CancellationTokenExtensions
{
    /// <summary>
    /// Creates a linked cancellation token with timeout.
    /// </summary>
    /// <param name="cancellationToken">The original cancellation token.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <returns>A linked cancellation token source.</returns>
    public static CancellationTokenSource CreateLinkedTokenSourceWithTimeout(
        this CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    /// <summary>
    /// Throws if cancellation is requested with a custom message.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="message">The message to include in the exception.</param>
    public static void ThrowIfCancellationRequested(
        this CancellationToken cancellationToken,
        string message)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(message, cancellationToken);
        }
    }

    /// <summary>
    /// Executes an action with cancellation check between iterations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="count">Number of iterations.</param>
    public static async Task ExecuteWithCancellationAsync(
        this CancellationToken cancellationToken,
        Func<int, Task> action,
        int count)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action(i);
        }
    }
}