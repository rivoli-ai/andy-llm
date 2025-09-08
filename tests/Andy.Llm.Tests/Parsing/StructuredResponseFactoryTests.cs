using System;
using System.Collections.Generic;
using System.Text.Json;
using Andy.Llm.Parsing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Parsing;

public class StructuredResponseFactoryTests
{
    private readonly Mock<ILogger<StructuredResponseFactory>> _mockLogger;
    private readonly StructuredResponseFactory _factory;

    public StructuredResponseFactoryTests()
    {
        _mockLogger = new Mock<ILogger<StructuredResponseFactory>>();
        _factory = new StructuredResponseFactory(_mockLogger.Object);
    }

    [Fact]
    public void CreateFromOpenAI_ValidChatCompletion_ExtractsAllFields()
    {
        // Arrange
        var openAiJson = @"{
            ""id"": ""chatcmpl-123"",
            ""object"": ""chat.completion"",
            ""created"": 1677652288,
            ""model"": ""gpt-4-0125-preview"",
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""I'll help you calculate that."",
                    ""tool_calls"": [
                        {
                            ""id"": ""call_abc123"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""calculate_sum"",
                                ""arguments"": ""{\""numbers\"": [1, 2, 3]}""
                            }
                        }
                    ]
                },
                ""finish_reason"": ""tool_calls""
            }],
            ""usage"": {
                ""prompt_tokens"": 50,
                ""completion_tokens"": 30,
                ""total_tokens"": 80
            }
        }";

        // Act
        var result = _factory.CreateFromOpenAI(openAiJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("I'll help you calculate that.", result.TextContent);
        Assert.Equal("openai", result.Metadata.Provider);
        Assert.Equal("gpt-4-0125-preview", result.Metadata.Model);
        Assert.Equal("tool_calls", result.Metadata.FinishReason);

        Assert.Single(result.ToolCalls);
        Assert.Equal("call_abc123", result.ToolCalls[0].Id);
        Assert.Equal("calculate_sum", result.ToolCalls[0].Name);
        Assert.NotNull(result.ToolCalls[0].Arguments);

        Assert.NotNull(result.Metadata.Usage);
        Assert.Equal(50, result.Metadata.Usage.InputTokens);
        Assert.Equal(30, result.Metadata.Usage.OutputTokens);
        Assert.Equal(80, result.Metadata.Usage.TotalTokens);
    }

    [Fact]
    public void CreateFromOpenAI_FunctionCallFormat_ParsesCorrectly()
    {
        // Arrange
        var functionCallJson = @"{
            ""message"": {
                ""content"": ""Let me search for that."",
                ""function_call"": {
                    ""name"": ""web_search"",
                    ""arguments"": ""{\""query\"": \""latest AI news\""}""
                }
            },
            ""model"": ""gpt-3.5-turbo""
        }";

        // Act
        var result = _factory.CreateFromOpenAI(functionCallJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Let me search for that.", result.TextContent);
        Assert.Single(result.ToolCalls);
        Assert.Equal("web_search", result.ToolCalls[0].Name);
        Assert.Contains("func_", result.ToolCalls[0].Id); // Generated ID
    }

    [Fact]
    public void CreateFromAnthropic_ValidMessage_ExtractsAllFields()
    {
        // Arrange
        var anthropicJson = @"{
            ""id"": ""msg_123"",
            ""type"": ""message"",
            ""role"": ""assistant"",
            ""content"": [
                {
                    ""type"": ""text"",
                    ""text"": ""I'll search for information about that topic.""
                },
                {
                    ""type"": ""tool_use"",
                    ""id"": ""toolu_abc123"",
                    ""name"": ""search_knowledge"",
                    ""input"": {
                        ""query"": ""quantum computing basics"",
                        ""limit"": 5
                    }
                }
            ],
            ""model"": ""claude-3-opus-20240229"",
            ""stop_reason"": ""tool_use"",
            ""stop_sequence"": null,
            ""usage"": {
                ""input_tokens"": 120,
                ""output_tokens"": 45
            }
        }";

        // Act
        var result = _factory.CreateFromAnthropic(anthropicJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("I'll search for information about that topic.", result.TextContent);
        Assert.Equal("anthropic", result.Metadata.Provider);
        Assert.Equal("claude-3-opus-20240229", result.Metadata.Model);
        Assert.Equal("tool_use", result.Metadata.FinishReason);

        Assert.Single(result.ToolCalls);
        Assert.Equal("toolu_abc123", result.ToolCalls[0].Id);
        Assert.Equal("search_knowledge", result.ToolCalls[0].Name);
        Assert.NotNull(result.ToolCalls[0].Arguments);
        // Arguments are JsonElements
        var queryArg = result.ToolCalls[0].Arguments["query"] as JsonElement?;
        Assert.Equal("quantum computing basics", queryArg?.GetString());

        Assert.NotNull(result.Metadata.Usage);
        Assert.Equal(120, result.Metadata.Usage.InputTokens);
        Assert.Equal(45, result.Metadata.Usage.OutputTokens);
    }

    [Fact]
    public void CreateFromAnthropic_MultipleContentBlocks_ParsesAll()
    {
        // Arrange
        var multiBlockJson = @"{
            ""content"": [
                {""type"": ""text"", ""text"": ""First part.""},
                {""type"": ""text"", ""text"": ""Second part.""},
                {
                    ""type"": ""tool_use"",
                    ""id"": ""tool1"",
                    ""name"": ""function1"",
                    ""input"": {}
                },
                {
                    ""type"": ""tool_result"",
                    ""tool_use_id"": ""tool1"",
                    ""content"": ""Result data""
                }
            ],
            ""model"": ""claude-3-sonnet""
        }";

        // Act
        var result = _factory.CreateFromAnthropic(multiBlockJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("First part.\nSecond part.", result.TextContent);
        Assert.Single(result.ToolCalls);
        Assert.Single(result.ToolResults);
        Assert.Equal("tool1", result.ToolResults[0].CallId);
    }

    [Fact]
    public void CreateFromGeneric_WithToolCallsData_ParsesCorrectly()
    {
        // Arrange
        var toolCallsData = new object[]
        {
            new { id = "call1", name = "func1", arguments = @"{""param"": ""value""}" },
            new { id = "call2", name = "func2", parameters = new { key = "val" } }
        };

        // Act
        var result = _factory.CreateFromGeneric("Text content", toolCallsData);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Text content", result.TextContent);
        Assert.Equal("generic", result.Metadata.Provider);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Equal("func1", result.ToolCalls[0].Name);
        Assert.Equal("func2", result.ToolCalls[1].Name);
    }

    [Fact]
    public void SafeParseArguments_ValidJson_ReturnsArguments()
    {
        // Arrange
        var json = @"{""key1"": ""value1"", ""key2"": 42, ""key3"": true}";

        // Act
        var (arguments, error) = StructuredArgumentParser.SafeParseArguments(json);

        // Assert
        Assert.NotNull(arguments);
        Assert.Null(error);

        // System.Text.Json deserializes to JsonElement when using Dictionary<string, object?>
        var key1 = arguments!["key1"] as JsonElement?;
        Assert.Equal("value1", key1?.GetString());

        var key2 = arguments["key2"] as JsonElement?;
        Assert.Equal(42, key2?.GetInt32());

        var key3 = arguments["key3"] as JsonElement?;
        Assert.True(key3?.GetBoolean());
    }

    [Fact]
    public void SafeParseArguments_InvalidJson_ReturnsError()
    {
        // Arrange
        var invalidJson = @"{""key"": invalid}";

        // Act
        var (arguments, error) = StructuredArgumentParser.SafeParseArguments(invalidJson);

        // Assert
        Assert.Null(arguments);
        Assert.NotNull(error);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("Error parsing tool call arguments", error.Message);
    }

    [Fact]
    public void SafeParseArguments_EmptyString_ReturnsEmptyDictionary()
    {
        // Act
        var (arguments, error) = StructuredArgumentParser.SafeParseArguments("");

        // Assert
        Assert.NotNull(arguments);
        Assert.Null(error);
        Assert.Empty(arguments);
    }

    [Fact]
    public void CreateToolCall_ValidArguments_CreatesWithParsedArgs()
    {
        // Arrange
        var id = "test_id";
        var name = "test_function";
        var argsJson = @"{""param"": ""value""}";

        // Act
        var toolCall = StructuredArgumentParser.CreateToolCall(id, name, argsJson);

        // Assert
        Assert.NotNull(toolCall);
        Assert.Equal(id, toolCall.Id);
        Assert.Equal(name, toolCall.Name);
        Assert.Equal(argsJson, toolCall.ArgumentsJson);
        Assert.NotNull(toolCall.Arguments);
        Assert.Null(toolCall.ParseError);
        // Arguments are JsonElements
        var paramArg = toolCall.Arguments!["param"] as JsonElement?;
        Assert.Equal("value", paramArg?.GetString());
    }

    [Fact]
    public void CreateToolCall_InvalidArguments_CreatesWithError()
    {
        // Arrange
        var id = "test_id";
        var name = "test_function";
        var invalidJson = @"{invalid json}";

        // Act
        var toolCall = StructuredArgumentParser.CreateToolCall(id, name, invalidJson);

        // Assert
        Assert.NotNull(toolCall);
        Assert.Equal(id, toolCall.Id);
        Assert.Equal(name, toolCall.Name);
        Assert.Equal(invalidJson, toolCall.ArgumentsJson);
        Assert.Null(toolCall.Arguments);
        Assert.NotNull(toolCall.ParseError);
    }

    [Fact]
    public void CreateFromOpenAI_MalformedJson_ReturnsBasicResponse()
    {
        // Arrange
        var malformedJson = "This is not JSON at all";

        // Act
        var result = _factory.CreateFromOpenAI(malformedJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("openai", result.Metadata.Provider);
        Assert.Equal(malformedJson, result.TextContent);
        Assert.Empty(result.ToolCalls);
    }

    [Fact]
    public void CreateFromAnthropic_WithToolResult_ParsesCorrectly()
    {
        // Arrange
        var toolResultJson = @"{
            ""content"": [
                {
                    ""type"": ""tool_result"",
                    ""tool_use_id"": ""toolu_123"",
                    ""content"": {""result"": ""Success"", ""data"": 42}
                }
            ]
        }";

        // Act
        var result = _factory.CreateFromAnthropic(toolResultJson);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.ToolResults);
        Assert.Equal("toolu_123", result.ToolResults[0].CallId);
        Assert.True(result.ToolResults[0].IsSuccess);
    }

    [Fact]
    public void CreateFromOpenAI_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _factory.CreateFromOpenAI(null!));
    }

    [Fact]
    public void CreateFromAnthropic_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _factory.CreateFromAnthropic(null!));
    }
}
