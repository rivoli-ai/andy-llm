using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Services;
using Xunit;

namespace Andy.Llm.Tests.Services;

public class ParameterCorrectionServiceTests
{
    [Fact]
    public void SuggestCorrections_ShouldRemapNearMissKeys()
    {
        var tools = new List<ToolDeclaration>
        {
            new ToolDeclaration
            {
                Name = "get_weather",
                Description = "Weather",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["location"] = new { type = "string" },
                        ["unit"] = new { type = "string" }
                    }
                }
            }
        };

        var call = new ToolCall
        {
            Id = "call_1",
            Name = "get_weather",
            ArgumentsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["locatio"] = "NYC", // near-miss
                ["unit"] = "c"
            })
        };

        var corrected = ParameterCorrectionService.SuggestCorrections(call, tools);

        Assert.NotNull(corrected);
        Assert.False(corrected!.ContainsKey("locatio"));
        Assert.Equal("NYC", corrected["location"]);
        Assert.Equal("c", corrected["unit"]);
    }
}


