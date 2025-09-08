using Andy.Llm.Resilience;
using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using System.Net;
using Xunit;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for resilience policies and retry mechanisms.
/// </summary>
public class ResilienceTests
{
    private readonly Mock<ILogger> _mockLogger;

    public ResilienceTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    [Fact]
    public async Task GetRetryPolicy_ShouldRetryOnTransientErrors()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_mockLogger.Object, maxRetryAttempts: 2);
        var attemptCount = 0;
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupSequence()
            .ReturnsResponse(HttpStatusCode.InternalServerError)
            .ReturnsResponse(HttpStatusCode.OK, "Success");

        var httpClient = new HttpClient(mockHandler);

        // Act
        var response = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            return await httpClient.GetAsync("http://test.com");
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task GetRetryPolicy_ShouldRetryOnTooManyRequests()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_mockLogger.Object, maxRetryAttempts: 3);
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupSequence()
            .ReturnsResponse(HttpStatusCode.TooManyRequests)
            .ReturnsResponse(HttpStatusCode.TooManyRequests)
            .ReturnsResponse(HttpStatusCode.OK, "Success");

        var httpClient = new HttpClient(mockHandler);

        // Act
        var response = await policy.ExecuteAsync(async () =>
        {
            return await httpClient.GetAsync("http://test.com");
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCircuitBreakerPolicy_ShouldOpenAfterConsecutiveFailures()
    {
        // Arrange
        var policy = ResiliencePolicies.GetCircuitBreakerPolicy(
            _mockLogger.Object,
            handledEventsAllowedBeforeBreaking: 2,
            TimeSpan.FromMilliseconds(100));

        var mockHandler = new MockHttpMessageHandler();
        mockHandler.AlwaysReturn(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(mockHandler);

        // Act - Trigger circuit breaker
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await policy.ExecuteAsync(async () =>
                    await httpClient.GetAsync("http://test.com"));
            }
            catch { }
        }

        // Assert - Circuit should be open
        await Assert.ThrowsAsync<BrokenCircuitException<HttpResponseMessage>>(async () =>
        {
            await policy.ExecuteAsync(async () =>
                await httpClient.GetAsync("http://test.com"));
        });
    }

    [Fact]
    public async Task GetTimeoutPolicy_ShouldTimeoutLongRunningRequests()
    {
        // Arrange
        var policy = ResiliencePolicies.GetTimeoutPolicy(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await policy.ExecuteAsync(async (ct) =>
            {
                // Use the cancellation token to properly respect timeouts
                await Task.Delay(200, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task GetCombinedPolicy_ShouldApplyAllPolicies()
    {
        // Arrange
        var options = new ResilienceOptions
        {
            MaxRetryAttempts = 2,
            CircuitBreakerThreshold = 5,
            Timeout = TimeSpan.FromSeconds(1),
            EnableRetry = true,
            EnableCircuitBreaker = true,
            EnableTimeout = true
        };

        var policy = ResiliencePolicies.GetCombinedPolicy(_mockLogger.Object, options);
        var attemptCount = 0;
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.SetupSequence()
            .ReturnsResponse(HttpStatusCode.ServiceUnavailable)
            .ReturnsResponse(HttpStatusCode.OK, "Success");

        var httpClient = new HttpClient(mockHandler);

        // Act
        var response = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            return await httpClient.GetAsync("http://test.com");
        });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, attemptCount); // Should retry once
    }

    [Fact]
    public async Task GetGenericRetryPolicy_ShouldRetryBasedOnPredicate()
    {
        // Arrange
        var attemptCount = 0;
        var policy = ResiliencePolicies.GetGenericRetryPolicy<string>(
            _mockLogger.Object,
            maxRetryAttempts: 2,
            shouldRetry: ex => ex is InvalidOperationException);

        // Act
        var result = await policy.ExecuteAsync(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
            {
                throw new InvalidOperationException("Transient error");
            }
            await Task.Delay(10);
            return "Success";
        });

        // Assert
        Assert.Equal("Success", result);
        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task GetGenericRetryPolicy_ShouldNotRetryNonMatchingExceptions()
    {
        // Arrange
        var policy = ResiliencePolicies.GetGenericRetryPolicy<string>(
            _mockLogger.Object,
            maxRetryAttempts: 2,
            shouldRetry: ex => ex is TimeoutException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await policy.ExecuteAsync(async () =>
            {
                await Task.Delay(10);
                throw new InvalidOperationException("Should not retry");
            });
        });
    }

    [Fact]
    public void ResilienceOptions_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new ResilienceOptions();

        // Assert
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(3, options.CircuitBreakerThreshold);
        Assert.Equal(TimeSpan.FromSeconds(30), options.CircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
        Assert.True(options.EnableRetry);
        Assert.True(options.EnableCircuitBreaker);
        Assert.True(options.EnableTimeout);
    }

    [Fact]
    public async Task RetryPolicy_ShouldUseExponentialBackoff()
    {
        // Arrange
        var policy = ResiliencePolicies.GetRetryPolicy(_mockLogger.Object, maxRetryAttempts: 3);
        var attemptTimes = new List<DateTime>();
        var mockHandler = new MockHttpMessageHandler();

        mockHandler.AlwaysReturn(HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(mockHandler);

        // Act
        try
        {
            await policy.ExecuteAsync(async () =>
            {
                attemptTimes.Add(DateTime.UtcNow);
                return await httpClient.GetAsync("http://test.com");
            });
        }
        catch { }

        // Assert - Verify exponential backoff timing
        Assert.Equal(4, attemptTimes.Count); // Initial + 3 retries

        // Each retry should have increasing delay
        for (int i = 1; i < attemptTimes.Count; i++)
        {
            var delay = attemptTimes[i] - attemptTimes[i - 1];
            var expectedMinDelay = TimeSpan.FromSeconds(Math.Pow(2, i - 1)) - TimeSpan.FromMilliseconds(100);
            Assert.True(delay >= expectedMinDelay, $"Retry {i} delay was too short");
        }
    }
}

/// <summary>
/// Mock HTTP message handler for testing HTTP policies.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private HttpResponseMessage? _defaultResponse;

    public MockHttpMessageHandler SetupSequence()
    {
        return this;
    }

    public MockHttpMessageHandler ReturnsResponse(HttpStatusCode statusCode, string content = "")
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        });
        return this;
    }

    public void AlwaysReturn(HttpStatusCode statusCode, string content = "")
    {
        _defaultResponse = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_responses.Count > 0)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        if (_defaultResponse != null)
        {
            return Task.FromResult(new HttpResponseMessage(_defaultResponse.StatusCode)
            {
                Content = _defaultResponse.Content
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
