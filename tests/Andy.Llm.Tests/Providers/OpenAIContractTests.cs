using Xunit;
using Andy.Llm.Models;
using Andy.Llm.Abstractions;
using Andy.Context.Model;
using Microsoft.Extensions.Logging;
using Moq;

namespace Andy.Llm.Tests.Providers;

public class OpenAIContractTests
{
    [Fact]
    public async Task CompleteAsync_ShouldReturnFunctionCalls_WithRawArgumentsJson()
    {
        var mockProvider = new Mock<ILlmProvider>();
        mockProvider.Setup(p => p.Name).Returns("Mock");

        var response = new LlmResponse
        {
            Content = string.Empty,
            FunctionCalls = new List<FunctionCall>
            {
                new FunctionCall
                {
                    Id = "call_1",
                    Name = "get_weather",
                    Arguments = new Dictionary<string, object?> { ["location"] = "Paris" },
                    ArgumentsJson = "{\"location\":\"Paris\"}"
                }
            },
            FinishReason = "tool_calls"
        };

        mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var logger = new Mock<ILogger<Andy.Llm.LlmClient>>();
        var client = new Andy.Llm.LlmClient(mockProvider.Object, logger.Object);

        var req = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "weather?" } },
            Functions = new List<ToolDeclaration> // alias should work
            {
                new ToolDeclaration { Name = "get_weather", Description = "Get weather", Parameters = new Dictionary<string, object>() }
            }
        };

        var result = await client.CompleteAsync(req);
        Assert.Single(result.FunctionCalls);
        Assert.Equal("get_weather", result.FunctionCalls[0].Name);
        Assert.Equal("{\"location\":\"Paris\"}", result.FunctionCalls[0].ArgumentsJson);
    }
}
