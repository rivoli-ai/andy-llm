using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Providers;

/// <summary>
/// Integration tests for Cerebras provider that make real API calls.
/// These tests require CEREBRAS_API_KEY environment variable to be set.
/// </summary>
public class CerebrasIntegrationTests
{
    private readonly ILogger<CerebrasProvider> _logger;

    public CerebrasIntegrationTests()
    {
        _logger = new Mock<ILogger<CerebrasProvider>>().Object;
    }

    /// <summary>
    /// Integration test: Verifies that llama-3.3-70b can make function calls via Cerebras API.
    /// This model is known to support function calling reliably.
    /// Requires CEREBRAS_API_KEY environment variable.
    /// </summary>
    [Fact]
    public async Task Llama33_WithFunctionCalling_ReturnsToolCall()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if API key not available
            return;
        }

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = apiKey,
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama-3.3-70b"
                }
            }
        });

        var provider = new CerebrasProvider(options, _logger);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "What is 15% of 240? Use the calculator tool." }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "calculate",
                    Description = "Perform a mathematical calculation",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["expression"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The mathematical expression to evaluate (e.g., '0.15 * 240')"
                            }
                        },
                        ["required"] = new[] { "expression" }
                    }
                }
            }
        };

        // Act
        var response = await provider.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.HasToolCalls, "llama-3.3-70b should return tool calls");
        Assert.NotEmpty(response.ToolCalls);

        var toolCall = response.ToolCalls[0];
        Assert.Equal("calculate", toolCall.Name);
        Assert.Contains("0.15", toolCall.ArgumentsJson);
        Assert.Contains("240", toolCall.ArgumentsJson);
    }

    /// <summary>
    /// Integration test: Verifies that gpt-oss-120b can make function calls via Cerebras API.
    /// Note: This model may occasionally hallucinate tool calls.
    /// Requires CEREBRAS_API_KEY environment variable.
    /// </summary>
    [Fact]
    public async Task GptOss120b_WithFunctionCalling_ReturnsToolCall()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if API key not available
            return;
        }

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = apiKey,
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "gpt-oss-120b"
                }
            }
        });

        var provider = new CerebrasProvider(options, _logger);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "List the files in the /workspace directory using the list_directory tool." }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "list_directory",
                    Description = "List files and directories in a given path",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The directory path to list"
                            }
                        },
                        ["required"] = new[] { "path" }
                    }
                }
            }
        };

        // Act
        var response = await provider.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.HasToolCalls, "gpt-oss-120b should return tool calls");
        Assert.NotEmpty(response.ToolCalls);

        var toolCall = response.ToolCalls[0];
        Assert.Equal("list_directory", toolCall.Name);
        Assert.Contains("workspace", toolCall.ArgumentsJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Integration test: Verifies that qwen-3-coder-480b can make function calls via Cerebras API.
    /// This is experimental - qwen-3-coder is primarily a code generation model.
    /// Requires CEREBRAS_API_KEY environment variable.
    /// </summary>
    [Fact]
    public async Task Qwen3Coder480b_WithFunctionCalling_ReturnsToolCall()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if API key not available
            return;
        }

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = apiKey,
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "qwen-3-coder-480b"
                }
            }
        });

        var provider = new CerebrasProvider(options, _logger);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "List the files in the /workspace directory using the list_directory tool." }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "list_directory",
                    Description = "List files and directories in a given path",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["path"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "The directory path to list"
                            }
                        },
                        ["required"] = new[] { "path" }
                    }
                }
            }
        };

        // Act
        var response = await provider.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.HasToolCalls, "qwen-3-coder-480b should return tool calls when provided with tools");
        Assert.NotEmpty(response.ToolCalls);

        var toolCall = response.ToolCalls[0];
        Assert.Equal("list_directory", toolCall.Name);
        Assert.Contains("workspace", toolCall.ArgumentsJson, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Integration test: Verifies that models without function calling support don't receive tools.
    /// This test will make a real API call but without tools, so should get a text response.
    /// Requires CEREBRAS_API_KEY environment variable.
    /// </summary>
    [Fact]
    public async Task Qwen25_7b_WithoutFunctionCalling_ReturnsTextResponse()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("CEREBRAS_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            // Skip test if API key not available
            return;
        }

        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = apiKey,
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "qwen-2.5-7b"
                }
            }
        });

        var provider = new CerebrasProvider(options, _logger);

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.User, Content = "What is 2+2?" }
            },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration
                {
                    Name = "calculate",
                    Description = "Perform a calculation",
                    Parameters = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>()
                    }
                }
            }
        };

        // Act
        var response = await provider.CompleteAsync(request);

        // Assert
        Assert.NotNull(response);
        // qwen-2.5-7b doesn't support function calling, so tools are filtered out
        // It should return a text response instead
        Assert.False(response.HasToolCalls, "qwen-2.5-7b should not receive tools and should return text");
        Assert.NotEmpty(response.Content);
        Assert.Contains("4", response.Content);
    }
}
