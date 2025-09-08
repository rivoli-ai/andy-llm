using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Andy.Llm.Abstractions;
using Andy.Llm.Models;
using Andy.Llm.Extensions;

namespace Andy.Llm.Tests;

/// <summary>
/// Provides test configuration for integration tests.
/// </summary>
public static class TestConfiguration
{
    private static IConfiguration? _configuration;

    /// <summary>
    /// Gets the test configuration.
    /// </summary>
    public static IConfiguration Configuration
    {
        get
        {
            if (_configuration == null)
            {
                _configuration = new ConfigurationBuilder()
                    .AddJsonFile("testsettings.json", optional: true)
                    .AddEnvironmentVariables()
                    .Build();
            }
            return _configuration;
        }
    }

    /// <summary>
    /// Determines if integration tests should be run.
    /// </summary>
    public static bool ShouldRunIntegrationTests()
    {
        // Check environment variable first
        var envFlag = Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS");
        if (!string.IsNullOrEmpty(envFlag))
        {
            return bool.TryParse(envFlag, out var result) && result;
        }

        // Check configuration
        return Configuration.GetValue<bool>("LlmTestSettings:RunIntegrationTests", false);
    }

    /// <summary>
    /// Gets the API key for a provider from environment or configuration.
    /// </summary>
    public static string? GetApiKey(string provider)
    {
        // Try environment variable first (CI/CD friendly)
        var envKey = provider.ToUpper() + "_API_KEY";
        var apiKey = Environment.GetEnvironmentVariable(envKey);

        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        // Fall back to test configuration
        return Configuration[$"LlmTestSettings:Providers:{provider}:ApiKey"];
    }

    /// <summary>
    /// Checks if a specific provider is enabled for testing.
    /// </summary>
    public static bool IsProviderEnabled(string provider)
    {
        // Check environment variable
        var envFlag = Environment.GetEnvironmentVariable($"TEST_{provider.ToUpper()}_ENABLED");
        if (!string.IsNullOrEmpty(envFlag))
        {
            return bool.TryParse(envFlag, out var result) && result;
        }

        // Check configuration
        return Configuration.GetValue<bool>($"LlmTestSettings:Providers:{provider}:Enabled", false);
    }

    /// <summary>
    /// Creates a service provider for testing.
    /// </summary>
    public static IServiceProvider CreateServiceProvider(bool useMocks = true)
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(Configuration.GetSection("Logging"));
            builder.AddConsole();
            builder.AddDebug();
        });

        // Add LLM services based on configuration
        if (useMocks || !ShouldRunIntegrationTests())
        {
            // Add mock implementations
            services.AddSingleton<ILlmProvider, MockLlmProvider>();
        }
        else
        {
            // Add real implementations with test configuration
            services.ConfigureLlmFromEnvironment();
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Gets the test timeout in milliseconds.
    /// </summary>
    public static int GetTestTimeout()
    {
        return Configuration.GetValue<int>("LlmTestSettings:TestTimeout", 30000);
    }
}

/// <summary>
/// Mock LLM provider for testing.
/// </summary>
public class MockLlmProvider : ILlmProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MockLlmProvider> _logger;

    public MockLlmProvider(IConfiguration configuration, ILogger<MockLlmProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public string Name => "Mock";

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        return true;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock provider handling request");
        await Task.Delay(100, cancellationToken);

        var response = _configuration["LlmTestSettings:MockResponses:DefaultResponse"]
            ?? "Mock response";

        return new LlmResponse
        {
            Content = response,
            Model = request.Model ?? "mock-model",
            Usage = new TokenUsage
            {
                PromptTokens = 10,
                CompletionTokens = 20,
                TotalTokens = 30
            }
        };
    }

    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock provider handling streaming request");

        var response = _configuration["LlmTestSettings:MockResponses:DefaultResponse"]
            ?? "Mock streaming response";

        var words = response.Split(' ');
        foreach (var word in words)
        {
            await Task.Delay(50, cancellationToken);
            yield return new LlmStreamResponse
            {
                TextDelta = word + " ",
                IsComplete = false
            };
        }

        yield return new LlmStreamResponse
        {
            IsComplete = true
        };
    }

    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);

        return new List<ModelInfo>
        {
            new ModelInfo
            {
                Id = "mock-model-1",
                Name = "Mock Model 1",
                Provider = "mock",
                Description = "Test model 1",
                MaxTokens = 4096
            },
            new ModelInfo
            {
                Id = "mock-model-2",
                Name = "Mock Model 2",
                Provider = "mock",
                Description = "Test model 2",
                MaxTokens = 8192
            }
        };
    }
}
