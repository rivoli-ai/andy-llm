using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Xunit;
using Andy.Llm.Providers;
using Moq;

namespace Andy.Llm.Tests.Providers;

public class OpenAIContractTests
{
    // TODO: Fix this test when LlmResponse properties have init setters
    // This test is commented out because LlmResponse properties are read-only in the published package
    /*
    [Fact]
    public async Task CompleteAsync_ShouldReturnFunctionCalls_WithRawArgumentsJson()
    {
        var mockProvider = new Mock<Andy.Model.Llm.ILlmProvider>();
        mockProvider.Setup(p => p.Name).Returns("Mock");

        var response = new LlmResponse
        {
            Content = string.Empty,
            FunctionCalls = new List<Andy.Model.Tooling.FunctionCall>
            {
                new Andy.Model.Tooling.FunctionCall
                {
                    Id = "call_1",
                    Name = "get_weather",
                    Arguments = "{\"location\":\"Paris\"}"
                }
            },
            FinishReason = "tool_calls"
        };

        mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Use provider directly - LlmClient has been removed

        var req = new LlmRequest
        {
            Messages = new List<Message> { new Message { Role = Role.User, Content = "weather?" } },
            Tools = new List<ToolDeclaration>
            {
                new ToolDeclaration { Name = "get_weather", Description = "Get weather", Parameters = new Dictionary<string, object>() }
            }
        };

        var result = await mockProvider.Object.CompleteAsync(req);
        Assert.Single(result.FunctionCalls);
        Assert.Equal("get_weather", result.FunctionCalls[0].Name);
        Assert.Equal("{\"location\":\"Paris\"}", result.FunctionCalls[0].Arguments);
    }
    */
}
