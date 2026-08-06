using System.Text;
using Andy.Llm.StructuredOutput;
using Xunit;

namespace Andy.Llm.Tests.StructuredOutput;

/// <summary>
/// Tests for <see cref="JsonSchemaPolicy"/> (LLM-I102): size and complexity limits,
/// reference rejection, unsupported vocabulary, and malformed schemas — all reported
/// as typed issues rather than exceptions.
/// </summary>
public class JsonSchemaPolicyTests
{
    [Fact]
    public void Valid_Schema_Within_Subset_Passes()
    {
        var schema = """
            {
              "type": "object",
              "required": ["name"],
              "additionalProperties": false,
              "properties": {
                "name": { "type": "string", "minLength": 1, "maxLength": 100 },
                "age": { "type": "integer", "minimum": 0, "maximum": 150 },
                "tags": { "type": "array", "items": { "type": "string" }, "uniqueItems": true },
                "kind": { "type": "string", "enum": ["a", "b"], "description": "annotation ok" }
              }
            }
            """;

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Oversized_Schema_Is_Reported_As_TooLarge()
    {
        var schema = "{ \"type\": \"object\", \"description\": \"" + new string('x', 70 * 1024) + "\" }";

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(JsonSchemaIssueKind.TooLarge, issue.Kind);
        Assert.Equal("$", issue.Path);
    }

    [Fact]
    public void Byte_Limit_Is_Configurable()
    {
        var schema = "{ \"type\": \"string\" }";
        var limits = new JsonSchemaLimits { MaxSchemaBytes = 8 };

        var result = JsonSchemaPolicy.Validate(schema, limits);

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.TooLarge, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public void Excessive_Nesting_Is_Reported_As_TooDeep_With_Path()
    {
        // 40 levels of nested "items" schemas (default depth limit is 32).
        var schema = new StringBuilder("{ \"type\": \"array\", \"items\": ");
        for (var i = 0; i < 40; i++)
        {
            schema.Append("{ \"type\": \"array\", \"items\": ");
        }

        schema.Append("{ \"type\": \"string\" }");
        schema.Append('}', 41);

        var result = JsonSchemaPolicy.Validate(schema.ToString());

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Kind == JsonSchemaIssueKind.TooDeep);
    }

    [Fact]
    public void Excessive_Keyword_Count_Is_Reported_As_TooManyKeywords()
    {
        var properties = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"\"p{i}\": {{ \"type\": \"string\" }}"));
        var schema = "{ \"type\": \"object\", \"properties\": { " + properties + " } }";
        var limits = new JsonSchemaLimits { MaxKeywords = 50 };

        var result = JsonSchemaPolicy.Validate(schema, limits);

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.TooManyKeywords, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public void Ref_Usage_Is_Reported_As_RecursiveReference()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "child": { "$ref": "#/properties/child" }
              }
            }
            """;

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(JsonSchemaIssueKind.RecursiveReference, issue.Kind);
        Assert.Equal("$.properties.child.$ref", issue.Path);
    }

    [Fact]
    public void Self_Contained_Schema_With_Defs_Is_Reported_As_UnsupportedKeyword()
    {
        var schema = """
            {
              "type": "object",
              "$defs": { "thing": { "type": "string" } },
              "properties": { "a": { "type": "string" } }
            }
            """;

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(JsonSchemaIssueKind.UnsupportedKeyword, issue.Kind);
        Assert.Contains("$defs", issue.Message);
    }

    [Theory]
    [InlineData("patternProperties", "$.patternProperties")]
    [InlineData("if", "$.if")]
    [InlineData("contains", "$.contains")]
    [InlineData("propertyNames", "$.propertyNames")]
    [InlineData("allOf", "$.allOf")]
    public void Unsupported_Keywords_Name_Keyword_And_Path(string keyword, string expectedPath)
    {
        var schema = "{ \"type\": \"object\", \"" + keyword + "\": {} }";

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(JsonSchemaIssueKind.UnsupportedKeyword, issue.Kind);
        Assert.Equal(expectedPath, issue.Path);
        Assert.Contains(keyword, issue.Message);
    }

    [Fact]
    public void Unsupported_Keyword_In_Nested_Schema_Reports_Nested_Path()
    {
        var schema = """
            {
              "type": "object",
              "properties": {
                "tasks": {
                  "type": "array",
                  "items": { "type": "object", "then": {} }
                }
              }
            }
            """;

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(JsonSchemaIssueKind.UnsupportedKeyword, issue.Kind);
        Assert.Equal("$.properties.tasks.items.then", issue.Path);
    }

    [Fact]
    public void Property_Names_Are_Not_Treated_As_Keywords()
    {
        // "if" as a *property name* under "properties" is data, not a keyword.
        var schema = """
            {
              "type": "object",
              "properties": {
                "if": { "type": "string" },
                "contains": { "type": "integer" }
              }
            }
            """;

        var result = JsonSchemaPolicy.Validate(schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Invalid_Json_Is_Reported_As_MalformedSchema()
    {
        var result = JsonSchemaPolicy.Validate("{ not json");

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.MalformedSchema, Assert.Single(result.Issues).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Schema_Is_Reported_As_MalformedSchema(string? schema)
    {
        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.MalformedSchema, Assert.Single(result.Issues).Kind);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("[1, 2]")]
    [InlineData("\"schema\"")]
    public void Non_Object_Root_Is_Reported_As_MalformedSchema(string schema)
    {
        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.MalformedSchema, Assert.Single(result.Issues).Kind);
    }

    [Theory]
    [InlineData("{ \"type\": \"giraffe\" }")]
    [InlineData("{ \"type\": 42 }")]
    [InlineData("{ \"type\": [] }")]
    [InlineData("{ \"type\": \"object\", \"required\": \"name\" }")]
    [InlineData("{ \"type\": \"object\", \"properties\": [] }")]
    [InlineData("{ \"type\": \"array\", \"items\": [{ \"type\": \"string\" }] }")]
    [InlineData("{ \"type\": \"string\", \"enum\": [] }")]
    public void Invalid_Keyword_Shapes_Are_Reported_As_MalformedSchema(string schema)
    {
        var result = JsonSchemaPolicy.Validate(schema);

        Assert.False(result.IsValid);
        Assert.Equal(JsonSchemaIssueKind.MalformedSchema, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public void Type_Arrays_Are_Accepted()
    {
        var result = JsonSchemaPolicy.Validate("{ \"type\": [\"string\", \"null\"] }");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validation_Never_Throws_On_Adversarial_Input()
    {
        foreach (var input in new[] { "\0", "{}", "{ \"type\": \"object\", \"properties\": { \"\": true } }", "false".PadLeft(100) })
        {
            var result = JsonSchemaPolicy.Validate(input);
            Assert.NotNull(result.Issues);
        }
    }
}
