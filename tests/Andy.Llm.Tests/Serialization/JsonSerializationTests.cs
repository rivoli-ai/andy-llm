using Andy.Llm.Serialization;
using Xunit;

namespace Andy.Llm.Tests.Serialization;

public class JsonSerializationTests
{
    [Fact]
    public void SerializeCamelCase_ShouldConvertPropertyNames()
    {
        var input = new { FirstName = "Ada", LastName = "Lovelace" };
        var json = JsonSerialization.SerializeCamelCase(input);
        Assert.Contains("firstName", json);
        Assert.Contains("lastName", json);
        Assert.DoesNotContain("FirstName", json);
    }

    [Fact]
    public void StableStringify_ShouldOrderDictionaryKeys()
    {
        var dict = new Dictionary<string, object?>
        {
            ["b"] = 2,
            ["a"] = 1
        };
        var json = JsonSerialization.StableStringify(dict);
        var indexA = json.IndexOf("\"a\"");
        var indexB = json.IndexOf("\"b\"");
        Assert.True(indexA < indexB);
    }
}


