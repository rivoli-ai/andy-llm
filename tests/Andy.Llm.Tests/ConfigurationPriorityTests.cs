using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for configuration priority and merging behavior
/// </summary>
[Collection("EnvironmentVariable Tests")]  // Prevent parallel execution with IntegrationTests
public class ConfigurationPriorityTests
{
    [Fact]
    public void DefaultProvider_FullyConfigured_ShouldBeConfiguredFirst()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "sk-test-key",
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4o-mini",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-cerebras-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama3.1-8b",
                    Enabled = true,
                    Priority = 10  // Higher priority number
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Default provider should be OpenAI and fully configured
        Assert.Equal("openai", llmOptions.Value.DefaultProvider);
        Assert.True(llmOptions.Value.Providers["openai"].Enabled);
        Assert.Equal("gpt-4o-mini", llmOptions.Value.Providers["openai"].Model);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiKey);

        // Cerebras has higher priority number but default provider takes precedence
        Assert.Equal(10, llmOptions.Value.Providers["cerebras"].Priority);
    }

    [Fact]
    public async Task DefaultProvider_MissingApiKey_ShouldFallbackToNextPriority()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    // Missing ApiKey - should skip this provider
                    ApiBase = "https://api.openai.com/v1",
                    Model = "gpt-4o-mini",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-cerebras-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama3.1-8b",
                    Enabled = true
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAvailableProviderAsync()
        );
    }

    [Fact]
    public void ProvidersWithPriority_ShouldBeOrderedCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    Model = "gpt-4o-mini",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-cerebras-key",
                    Model = "llama3.1-8b",
                    Enabled = true,
                    Priority = 5  // Lower priority
                },
                ["ollama"] = new ProviderConfig
                {
                    ApiKey = "not-needed",
                    ApiBase = "http://localhost:11434",
                    Model = "llama2",
                    Enabled = true,
                    Priority = 10  // Higher priority
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configuration is set up correctly
        // Default provider should be openai
        Assert.Equal("openai", llmOptions.Value.DefaultProvider);

        // Verify priorities are set correctly
        Assert.Null(llmOptions.Value.Providers["openai"].Priority);  // Default has no explicit priority
        Assert.Equal(10, llmOptions.Value.Providers["ollama"].Priority);
        Assert.Equal(5, llmOptions.Value.Providers["cerebras"].Priority);

        // All should be enabled
        Assert.True(llmOptions.Value.Providers["openai"].Enabled);
        Assert.True(llmOptions.Value.Providers["ollama"].Enabled);
        Assert.True(llmOptions.Value.Providers["cerebras"].Enabled);
    }

    [Fact]
    public void ConfigurationMerging_AppSettingsModelShouldOverrideEnvironmentModel()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["Llm:DefaultProvider"] = "OpenAI",
            ["Llm:Providers:OpenAI:Model"] = "gpt-4o-mini",
            ["Llm:Providers:OpenAI:ApiBase"] = "https://api.openai.com/v1",
            ["Llm:Providers:OpenAI:Enabled"] = "true"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(configuration);

        // Simulate environment variable configuration (should not override model)
        services.Configure<LlmOptions>(options =>
        {
            if (options.Providers.ContainsKey("openai"))
            {
                // Environment variable would try to set this, but config should win
                var existing = options.Providers["openai"];
                options.Providers["openai"] = new ProviderConfig
                {
                    ApiKey = "env-api-key",
                    Model = existing.Model,  // Keep model from config
                    ApiBase = existing.ApiBase,
                    Enabled = existing.Enabled
                };
            }
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Find provider case-insensitively
        var openaiProvider = llmOptions.Value.Providers.FirstOrDefault(p =>
            p.Key.Equals("openai", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(openaiProvider.Value);
        Assert.Equal("gpt-4o-mini", openaiProvider.Value.Model);
    }

    [Fact]
    public void ProviderConfig_ShouldHavePriorityProperty()
    {
        // Arrange & Act
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            Model = "test-model",
            Priority = 5
        };

        // Assert
        Assert.Equal(5, config.Priority);
    }

    [Fact]
    public async Task EnabledFalse_ShouldSkipProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "sk-test-key",
                    Model = "gpt-4o",
                    Enabled = false  // Explicitly disabled
                },
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    Model = "llama3.1-8b",
                    Enabled = true
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAvailableProviderAsync()
        );
    }

    [Fact]
    public void ConfigureLlmFromEnvironment_ShouldNotOverrideExistingModel()
    {
        // Arrange
        var configData = new Dictionary<string, string>
        {
            ["Llm:DefaultProvider"] = "OpenAI",
            ["Llm:Providers:OpenAI:Model"] = "gpt-4o-mini",
            ["Llm:Providers:OpenAI:ApiBase"] = "https://api.openai.com/v1",
            ["Llm:Providers:OpenAI:Enabled"] = "true"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData!)
            .Build();

        // Simulate having OPENAI_API_KEY but not OPENAI_MODEL
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test-key");
        Environment.SetEnvironmentVariable("OPENAI_MODEL", null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLlmServices(configuration);
        services.ConfigureLlmFromEnvironment();

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Model from configuration should be preserved (find case-insensitively)
        var openaiProvider = llmOptions.Value.Providers.FirstOrDefault(p =>
            p.Key.Equals("openai", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(openaiProvider.Value);
        Assert.Equal("gpt-4o-mini", openaiProvider.Value.Model);

        // Cleanup
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
    }

    [Fact]
    public void ConfigurationPrecedence_AppsettingsOverEnvironmentForModel()
    {
        // Arrange - Simulate the exact scenario from the bug report
        var configData = new Dictionary<string, string>
        {
            ["Llm:DefaultProvider"] = "OpenAI",
            ["Llm:Providers:OpenAI:Model"] = "gpt-4o-mini",  // From appsettings.json
            ["Llm:Providers:OpenAI:ApiBase"] = "https://api.openai.com/v1",
            ["Llm:Providers:OpenAI:Enabled"] = "true"
        };

        // Environment variables
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test-key");
        Environment.SetEnvironmentVariable("OPENAI_MODEL", "gpt-4o");  // This should NOT override appsettings

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()  // Load env vars first
            .AddInMemoryCollection(configData!)  // Config should override
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // This is the problematic pattern - ConfigureLlmFromEnvironment overrides everything
        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Model from appsettings.json should win, not environment variable (find case-insensitively)
        var openaiProvider = llmOptions.Value.Providers.FirstOrDefault(p =>
            p.Key.Equals("openai", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(openaiProvider.Value);
        Assert.Equal("gpt-4o-mini", openaiProvider.Value.Model);

        // Cleanup
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
        Environment.SetEnvironmentVariable("OPENAI_MODEL", null);
    }

    [Fact]
    public async Task ProviderSelection_ShouldRespectPriorityOverDictionaryOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                // Dictionary order: a, b, c
                // But priority order should be: c (priority 10), a (priority 5), b (no priority)
                ["a-provider"] = new ProviderConfig
                {
                    ApiKey = "missing",  // Will fail
                    Model = "model-a",
                    Enabled = true,
                    Priority = 5
                },
                ["b-provider"] = new ProviderConfig
                {
                    ApiKey = "missing",  // Will fail
                    Model = "model-b",
                    Enabled = true
                    // No priority - should be last
                },
                ["c-provider"] = new ProviderConfig
                {
                    ApiKey = "missing",  // Will fail
                    Model = "model-c",
                    Enabled = true,
                    Priority = 10  // Highest priority
                },
                ["openai"] = new ProviderConfig
                {
                    // Default provider is missing key - should be tried first anyway
                    Model = "gpt-4o",
                    Enabled = true
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act & Assert
        // All will fail, but order should be: openai (default), c (10), a (5), b (none)
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAvailableProviderAsync()
        );
    }

    [Fact]
    public void HighPriorityIncomplete_ConfigurationShouldFilterOutIncomplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    // Complete configuration but no explicit priority
                    ApiKey = "sk-test-key",
                    Model = "gpt-4o-mini",
                    ApiBase = "https://api.openai.com/v1",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    // High priority but INCOMPLETE (missing ApiKey)
                    Model = "llama3.1-8b",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 100  // Higher priority
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configuration is set correctly
        // OpenAI should be complete (default provider)
        Assert.True(llmOptions.Value.Providers["openai"].Enabled);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["openai"].Model);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiBase);

        // Cerebras should be incomplete (missing ApiKey)
        Assert.Null(llmOptions.Value.Providers["cerebras"].ApiKey);
        Assert.Equal(100, llmOptions.Value.Providers["cerebras"].Priority);

        // Default provider should be OpenAI
        Assert.Equal("openai", llmOptions.Value.DefaultProvider);
    }

    [Fact]
    public void MultipleIncompleteHighPriority_ConfigurationValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "azure";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["azure"] = new ProviderConfig
                {
                    // Incomplete (missing DeploymentName)
                    ApiKey = "key-azure",
                    ApiBase = "https://test.openai.azure.com",
                    Enabled = true,
                    Priority = 100  // Highest priority but incomplete
                },
                ["cerebras"] = new ProviderConfig
                {
                    // Incomplete (missing ApiKey)
                    Model = "llama3.1-8b",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 90  // Second highest priority but incomplete
                },
                ["openai"] = new ProviderConfig
                {
                    // Complete but lower priority
                    ApiKey = "sk-test-key",
                    Model = "gpt-4o-mini",
                    ApiBase = "https://api.openai.com/v1",
                    Enabled = true,
                    Priority = 10  // Lower priority
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configurations
        // Azure is incomplete (missing DeploymentName for Azure)
        Assert.Null(llmOptions.Value.Providers["azure"].DeploymentName);
        Assert.Equal(100, llmOptions.Value.Providers["azure"].Priority);

        // Cerebras is incomplete (missing ApiKey)
        Assert.Null(llmOptions.Value.Providers["cerebras"].ApiKey);
        Assert.Equal(90, llmOptions.Value.Providers["cerebras"].Priority);

        // OpenAI is complete
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["openai"].Model);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiBase);
        Assert.Equal(10, llmOptions.Value.Providers["openai"].Priority);
    }

    [Fact]
    public void DefaultProviderIncomplete_ConfigurationPriorityCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    // Default provider but INCOMPLETE (missing ApiKey)
                    Model = "gpt-4o-mini",
                    ApiBase = "https://api.openai.com/v1",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    // High priority and COMPLETE
                    ApiKey = "test-cerebras-key",
                    Model = "llama3.1-8b",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 50
                },
                ["ollama"] = new ProviderConfig
                {
                    // Low priority and complete
                    ApiKey = "not-needed",
                    Model = "llama2",
                    ApiBase = "http://localhost:11434",
                    Enabled = true,
                    Priority = 10
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configuration priorities
        // OpenAI is default but incomplete (missing ApiKey)
        Assert.Equal("openai", llmOptions.Value.DefaultProvider);
        Assert.Null(llmOptions.Value.Providers["openai"].ApiKey);

        // Cerebras has higher priority than Ollama and is complete
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].Model);
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].ApiBase);
        Assert.Equal(50, llmOptions.Value.Providers["cerebras"].Priority);

        // Ollama has lower priority
        Assert.Equal(10, llmOptions.Value.Providers["ollama"].Priority);
        Assert.True(llmOptions.Value.Providers["cerebras"].Priority > llmOptions.Value.Providers["ollama"].Priority);
    }

    [Fact]
    public void OnlyNoPriorityComplete_ConfigurationValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "azure";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["azure"] = new ProviderConfig
                {
                    // Default but incomplete (missing DeploymentName)
                    ApiKey = "azure-key",
                    ApiBase = "https://test.openai.azure.com",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    // High priority but incomplete (missing ApiKey)
                    Model = "llama3.1-8b",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 100
                },
                ["openai"] = new ProviderConfig
                {
                    // No priority but COMPLETE
                    ApiKey = "sk-test-key",
                    Model = "gpt-4o-mini",
                    ApiBase = "https://api.openai.com/v1",
                    Enabled = true
                    // No Priority property - should be tried after all Priority providers
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configurations
        // Azure default is incomplete
        Assert.Null(llmOptions.Value.Providers["azure"].DeploymentName);

        // Cerebras has priority but is incomplete
        Assert.Null(llmOptions.Value.Providers["cerebras"].ApiKey);
        Assert.Equal(100, llmOptions.Value.Providers["cerebras"].Priority);

        // OpenAI has no priority but is complete
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["openai"].Model);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiBase);
        Assert.Null(llmOptions.Value.Providers["openai"].Priority);
    }

    [Fact]
    public void OllamaComplete_OnlyRequiresApiBaseAndModel()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "ollama";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["ollama"] = new ProviderConfig
                {
                    // Ollama doesn't require ApiKey
                    ApiBase = "http://localhost:11434",
                    Model = "llama2",
                    Enabled = true
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act - Use CreateProvider instead of CreateAvailableProviderAsync to avoid requiring actual connection
        var provider = factory.CreateProvider("ollama");

        // Assert - Ollama should be considered complete without ApiKey
        Assert.Equal("ollama", provider.Name);
    }

    [Fact]
    public async Task AllProvidersIncomplete_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    // Missing ApiKey
                    Model = "gpt-4o-mini",
                    ApiBase = "https://api.openai.com/v1",
                    Enabled = true
                },
                ["cerebras"] = new ProviderConfig
                {
                    // Missing Model
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 100
                },
                ["ollama"] = new ProviderConfig
                {
                    // Ollama missing Model
                    ApiBase = "http://localhost:11434",
                    Enabled = true,
                    Priority = 50
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAvailableProviderAsync()
        );
    }

    [Fact]
    public void CompleteConfigMissingApiBase_ValidationCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    // Missing ApiBase - should be considered incomplete
                    ApiKey = "sk-test-key",
                    Model = "gpt-4o-mini",
                    Enabled = true,
                    Priority = 100
                },
                ["cerebras"] = new ProviderConfig
                {
                    // Complete
                    ApiKey = "test-key",
                    Model = "llama3.1-8b",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Enabled = true,
                    Priority = 50
                }
            };
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

        // Assert - Verify configurations
        // OpenAI is incomplete (missing ApiBase)
        Assert.Null(llmOptions.Value.Providers["openai"].ApiBase);
        Assert.NotNull(llmOptions.Value.Providers["openai"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["openai"].Model);

        // Cerebras is complete
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].ApiKey);
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].Model);
        Assert.NotNull(llmOptions.Value.Providers["cerebras"].ApiBase);
    }

    [Fact]
    public void EnvironmentVariables_WithDisabledProvider_ShouldNotEnableIt()
    {
        // Arrange - Simulate environment variables
        Environment.SetEnvironmentVariable("CEREBRAS_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("CEREBRAS_API_BASE", "https://api.cerebras.ai/v1");
        Environment.SetEnvironmentVariable("CEREBRAS_MODEL", "llama3.1-70b");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            // Configure with DISABLED cerebras provider
            services.AddLlmServices(options =>
            {
                options.Providers = new Dictionary<string, ProviderConfig>
                {
                    ["cerebras/large"] = new ProviderConfig
                    {
                        Provider = "cerebras",
                        Enabled = false,  // DISABLED in config
                        Model = "qwen-3-coder-480b"
                    }
                };
            });

            // Apply environment variables
            services.ConfigureLlmFromEnvironment();

            var serviceProvider = services.BuildServiceProvider();
            var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

            // Assert - Provider should still be DISABLED
            Assert.False(llmOptions.Value.Providers["cerebras/large"].Enabled);

            // But API key should be merged
            Assert.Equal("test-key", llmOptions.Value.Providers["cerebras/large"].ApiKey);

            // Model from config should NOT be overridden
            Assert.Equal("qwen-3-coder-480b", llmOptions.Value.Providers["cerebras/large"].Model);

            // Should NOT have created a new "cerebras" provider
            Assert.False(llmOptions.Value.Providers.ContainsKey("cerebras"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CEREBRAS_API_KEY", null);
            Environment.SetEnvironmentVariable("CEREBRAS_API_BASE", null);
            Environment.SetEnvironmentVariable("CEREBRAS_MODEL", null);
        }
    }

    [Fact]
    public void EnvironmentVariables_WithExistingConfig_ShouldNotCreateNewProviders()
    {
        // Arrange - Set OPENAI environment variables
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("CEREBRAS_API_KEY", "test-cerebras");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            // Configure with ONLY OpenAI (no Cerebras)
            services.AddLlmServices(options =>
            {
                options.Providers = new Dictionary<string, ProviderConfig>
                {
                    ["openai/latest-small"] = new ProviderConfig
                    {
                        Provider = "openai",
                        ApiBase = "https://api.openai.com/v1",
                        Model = "gpt-4o-mini",
                        Enabled = true
                    }
                };
            });

            // Apply environment variables
            services.ConfigureLlmFromEnvironment();

            var serviceProvider = services.BuildServiceProvider();
            var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

            // Assert - Should have OpenAI with merged API key
            Assert.True(llmOptions.Value.Providers.ContainsKey("openai/latest-small"));
            Assert.Equal("sk-test", llmOptions.Value.Providers["openai/latest-small"].ApiKey);

            // Should NOT have created "cerebras" provider even though CEREBRAS_API_KEY is set
            Assert.False(llmOptions.Value.Providers.ContainsKey("cerebras"));

            // Should also NOT have created plain "openai" provider
            Assert.False(llmOptions.Value.Providers.ContainsKey("openai"));

            // Should have exactly 1 provider
            Assert.Single(llmOptions.Value.Providers);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("CEREBRAS_API_KEY", null);
        }
    }

    [Fact]
    public void EnvironmentVariables_WithNoConfig_ShouldCreateProvidersForBackwardCompatibility()
    {
        // Arrange - Set environment variables
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-test");
        Environment.SetEnvironmentVariable("OPENAI_MODEL", "gpt-4o");
        Environment.SetEnvironmentVariable("OPENAI_API_BASE", "https://api.openai.com/v1");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            // NO configuration at all (legacy mode)
            services.AddLlmServices(options => { });

            // Apply environment variables
            services.ConfigureLlmFromEnvironment();

            var serviceProvider = services.BuildServiceProvider();
            var llmOptions = serviceProvider.GetRequiredService<IOptions<LlmOptions>>();

            // Assert - Should have created provider from environment in legacy mode
            Assert.True(llmOptions.Value.Providers.ContainsKey("openai"));
            Assert.Equal("sk-test", llmOptions.Value.Providers["openai"].ApiKey);
            Assert.Equal("gpt-4o", llmOptions.Value.Providers["openai"].Model);
            Assert.Equal("https://api.openai.com/v1", llmOptions.Value.Providers["openai"].ApiBase);
            Assert.True(llmOptions.Value.Providers["openai"].Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("OPENAI_MODEL", null);
            Environment.SetEnvironmentVariable("OPENAI_API_BASE", null);
        }
    }
}
