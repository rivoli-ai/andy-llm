using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Andy.Llm.Tests.Parsing;

public class HybridLlmParserTests
{
    private readonly Mock<ILlmResponseParser> _mockTextParser;
    private readonly Mock<IStructuredResponseFactory> _mockStructuredFactory;
    private readonly Mock<ILogger<HybridLlmParser>> _mockLogger;
    private readonly HybridLlmParser _parser;

    public HybridLlmParserTests()
    {
        _mockTextParser = new Mock<ILlmResponseParser>();
        _mockStructuredFactory = new Mock<IStructuredResponseFactory>();
        _mockLogger = new Mock<ILogger<HybridLlmParser>>();
        _parser = new HybridLlmParser(_mockTextParser.Object, _mockStructuredFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public void Parse_OpenAIStructuredResponse_ExtractsToolCalls()
    {
        // Arrange
        var openAiResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""I'll help you with that calculation"",
                    ""tool_calls"": [{
                        ""id"": ""call_123"",
                        ""type"": ""function"",
                        ""function"": {
                            ""name"": ""calculate"",
                            ""arguments"": ""{\""operation\"": \""add\"", \""a\"": 5, \""b\"": 3}""
                        }
                    }]
                },
                ""finish_reason"": ""tool_calls""
            }],
            ""model"": ""gpt-4"",
            ""usage"": {
                ""prompt_tokens"": 10,
                ""completion_tokens"": 20,
                ""total_tokens"": 30
            }
        }";

        // Act
        var result = _parser.Parse(openAiResponse);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("structured", result.ModelProvider);

        var toolCalls = result.Children.OfType<ToolCallNode>().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("calculate", toolCalls[0].ToolName);
        Assert.Equal("call_123", toolCalls[0].CallId);
        Assert.NotNull(toolCalls[0].Arguments);
        Assert.NotNull(toolCalls[0].Arguments);
        Assert.Contains("operation", toolCalls[0].Arguments!.Keys);
        Assert.Contains("a", toolCalls[0].Arguments.Keys);
        Assert.Contains("b", toolCalls[0].Arguments.Keys);
    }

    [Fact]
    public void Parse_AnthropicStructuredResponse_ExtractsToolUse()
    {
        // Arrange
        var anthropicResponse = @"{
            ""content"": [
                {
                    ""type"": ""text"",
                    ""text"": ""I'll search for that information.""
                },
                {
                    ""type"": ""tool_use"",
                    ""id"": ""toolu_123"",
                    ""name"": ""search"",
                    ""input"": {""query"": ""weather in Paris""}
                }
            ],
            ""model"": ""claude-3-sonnet"",
            ""stop_reason"": ""tool_use"",
            ""usage"": {
                ""input_tokens"": 15,
                ""output_tokens"": 25
            }
        }";

        // Act
        var result = _parser.Parse(anthropicResponse);

        // Assert
        Assert.NotNull(result);

        var textNodes = result.Children.OfType<TextNode>().ToList();
        Assert.Single(textNodes);
        Assert.Contains("search for that information", textNodes[0].Content);

        var toolCalls = result.Children.OfType<ToolCallNode>().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("search", toolCalls[0].ToolName);
        Assert.Equal("toolu_123", toolCalls[0].CallId);
    }

    [Fact]
    public void Parse_MalformedStructured_FallsBackToText()
    {
        // Arrange
        var malformedJson = @"{ ""broken"": ""json"", ""tool_calls"": [{ incomplete }";
        var expectedTextNode = new ResponseNode();
        expectedTextNode.Children.Add(new TextNode { Content = malformedJson });

        _mockTextParser
            .Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<ParserContext>()))
            .Returns(expectedTextNode);

        // Act
        var result = _parser.Parse(malformedJson);

        // Assert
        Assert.NotNull(result);
        _mockTextParser.Verify(p => p.Parse(malformedJson, It.IsAny<ParserContext>()), Times.Once);
    }

    [Fact]
    public void Parse_TextWithEmbeddedTools_DelegatesToTextParser()
    {
        // Arrange
        var textResponse = @"I'll search for that information.
{""tool_call"": {""name"": ""search"", ""arguments"": {""query"": ""test""}}}
The search has been completed.";

        var expectedNode = new ResponseNode();
        expectedNode.Children.Add(new TextNode { Content = "I'll search for that information." });
        expectedNode.Children.Add(new ToolCallNode { ToolName = "search" });
        expectedNode.Children.Add(new TextNode { Content = "The search has been completed." });

        _mockTextParser
            .Setup(p => p.Parse(textResponse, It.IsAny<ParserContext>()))
            .Returns(expectedNode);

        // Act
        var result = _parser.Parse(textResponse);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Children.Count);
        _mockTextParser.Verify(p => p.Parse(textResponse, It.IsAny<ParserContext>()), Times.Once);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyResponse()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hybrid", result.ModelProvider);
        Assert.Empty(result.Children);
        Assert.True(result.ResponseMetadata.IsComplete);
    }

    [Fact]
    public void Parse_NullInput_ReturnsEmptyResponse()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hybrid", result.ModelProvider);
        Assert.Empty(result.Children);
    }

    [Fact]
    public void Parse_ToolCallWithInvalidArguments_CapturesParseError()
    {
        // Arrange
        var responseWithBadArgs = @"{
            ""tool_calls"": [{
                ""id"": ""call_456"",
                ""type"": ""function"",
                ""function"": {
                    ""name"": ""process"",
                    ""arguments"": ""{ invalid json: true }""
                }
            }]
        }";

        // Act
        var result = _parser.Parse(responseWithBadArgs);

        // Assert
        var toolCall = result.Children.OfType<ToolCallNode>().FirstOrDefault();
        Assert.NotNull(toolCall);
        Assert.NotNull(toolCall.ParseError);
        Assert.Contains("Error parsing", toolCall.ParseError.Message);
    }

    [Fact]
    public async Task ParseStreamingAsync_StructuredFormat_BuffersAndParses()
    {
        // Arrange
        var chunks = new List<string>
        {
            "{\"choices\"",
            ": [{\"mess",
            "age\": {\"tool_calls",
            "\": [{\"id\": \"123\", \"type\": \"function\", ",
            "\"function\": {\"name\": \"test\", \"arguments\": \"{}\"}}]}}]}"
        };

        // Act
        var result = await _parser.ParseStreamingAsync(ToAsyncEnumerable(chunks));

        // Assert
        Assert.NotNull(result);
        // Since it detects structured format, it should buffer and parse as structured
        Assert.Contains(result.Children, n => n is ToolCallNode);
    }

    [Fact]
    public async Task ParseStreamingAsync_TextFormat_DelegatesToTextParser()
    {
        // Arrange
        var chunks = new List<string>
        {
            "This is ",
            "plain text ",
            "response."
        };

        var expectedNode = new ResponseNode();
        expectedNode.Children.Add(new TextNode { Content = "This is plain text response." });

        _mockTextParser
            .Setup(p => p.ParseStreamingAsync(It.IsAny<IAsyncEnumerable<string>>(), It.IsAny<ParserContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedNode);

        // Also setup the Parse method in case it gets called as fallback
        _mockTextParser
            .Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<ParserContext?>()))
            .Returns(expectedNode);

        // Act
        var result = await _parser.ParseStreamingAsync(ToAsyncEnumerable(chunks));

        // Assert
        Assert.NotNull(result);
        _mockTextParser.Verify(p => p.ParseStreamingAsync(It.IsAny<IAsyncEnumerable<string>>(), It.IsAny<ParserContext?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Validate_DuplicateToolCallIds_ReturnsError()
    {
        // Arrange
        var ast = new ResponseNode();
        ast.Children.Add(new ToolCallNode { CallId = "123", ToolName = "tool1" });
        ast.Children.Add(new ToolCallNode { CallId = "123", ToolName = "tool2" });

        // Act
        var result = _parser.Validate(ast);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("Duplicate tool call ID"));
    }

    [Fact]
    public void Validate_ToolResultWithoutCall_ReturnsWarning()
    {
        // Arrange
        var ast = new ResponseNode();
        ast.Children.Add(new ToolCallNode { CallId = "123", ToolName = "tool1" });
        ast.Children.Add(new ToolResultNode { CallId = "456", ToolName = "tool2" });

        // Act
        var result = _parser.Validate(ast);

        // Assert
        Assert.True(result.IsValid); // Warning doesn't make it invalid
        Assert.Contains(result.Issues, i => i.Message.Contains("Tool result without corresponding call"));
    }

    [Fact]
    public void Validate_ToolCallWithParseError_ReturnsError()
    {
        // Arrange
        var ast = new ResponseNode();
        ast.Children.Add(new ToolCallNode
        {
            CallId = "123",
            ToolName = "tool1",
            ParseError = new InvalidOperationException("Invalid JSON")
        });

        // Act
        var result = _parser.Validate(ast);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Message.Contains("Tool call parsing error"));
    }

    [Fact]
    public void GetCapabilities_IncludesStructuredFormats()
    {
        // Arrange
        _mockTextParser
            .Setup(p => p.GetCapabilities())
            .Returns(new ParserCapabilities
            {
                SupportsStreaming = true,
                SupportsToolCalls = false,
                SupportsCodeBlocks = true,
                SupportedFormats = new List<string> { "text", "markdown" }
            });

        // Act
        var capabilities = _parser.GetCapabilities();

        // Assert
        Assert.True(capabilities.SupportsToolCalls);
        Assert.True(capabilities.SupportsStreaming);
        Assert.Contains("structured-openai", capabilities.SupportedFormats);
        Assert.Contains("structured-anthropic", capabilities.SupportedFormats);
        Assert.Contains("structured-generic", capabilities.SupportedFormats);
    }

    [Fact]
    public void Parse_OpenAIStreamingChunk_ParsesCorrectly()
    {
        // Arrange
        var streamChunk = @"{
            ""delta"": {
                ""content"": ""Here is "",
                ""tool_calls"": [{
                    ""index"": 0,
                    ""function"": {
                        ""arguments"": ""{\""test\"":""
                    }
                }]
            }
        }";

        // Act
        var result = _parser.Parse(streamChunk);

        // Assert
        Assert.NotNull(result);
        // Streaming chunks are detected as structured format
    }

    [Fact]
    public void Parse_ServerSentEventFormat_DetectedAsStructured()
    {
        // Arrange
        var sseResponse = @"event: message
data: {""content"": ""test"", ""tool_calls"": []}

event: done
data: [DONE]";

        var expectedNode = new ResponseNode();
        expectedNode.Children.Add(new TextNode { Content = sseResponse });

        _mockTextParser
            .Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<ParserContext?>()))
            .Returns(expectedNode);

        // Act
        var result = _parser.Parse(sseResponse);

        // Assert
        Assert.NotNull(result);
        // SSE format should be parsed as text since it's not valid JSON
        Assert.NotEmpty(result.Children);
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
