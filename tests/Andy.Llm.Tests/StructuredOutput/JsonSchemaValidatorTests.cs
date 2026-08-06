using System.Text.Json;
using Andy.Llm.StructuredOutput;
using Xunit;

namespace Andy.Llm.Tests.StructuredOutput;

/// <summary>
/// Direct tests for <see cref="JsonSchemaValidator"/>, covering each enforced keyword of the
/// supported subset against <see cref="System.Text.Json.JsonElement"/> instances.
/// </summary>
public class JsonSchemaValidatorTests
{
    private static SchemaValidationResult Validate(string instance, string schema)
    {
        using var document = JsonDocument.Parse(instance);
        return JsonSchemaValidator.Validate(document.RootElement, schema);
    }

    [Fact]
    public void Conforming_Instance_Passes()
    {
        var result = Validate(
            "{ \"name\": \"Ada\", \"age\": 36, \"tags\": [\"a\", \"b\"] }",
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name"],
              "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" },
                "tags": { "type": "array", "items": { "type": "string" } }
              }
            }
            """);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1", "{ \"type\": \"integer\" }", true)]
    [InlineData("1.5", "{ \"type\": \"integer\" }", false)]
    [InlineData("1.5", "{ \"type\": \"number\" }", true)]
    [InlineData("1", "{ \"type\": \"number\" }", true)]
    [InlineData("\"x\"", "{ \"type\": [\"string\", \"null\"] }", true)]
    [InlineData("null", "{ \"type\": [\"string\", \"null\"] }", true)]
    [InlineData("42", "{ \"type\": [\"string\", \"null\"] }", false)]
    [InlineData("true", "{ \"type\": \"boolean\" }", true)]
    public void Type_Keyword_Is_Enforced_Including_Type_Arrays(string instance, string schema, bool expectedValid)
    {
        Assert.Equal(expectedValid, Validate(instance, schema).IsValid);
    }

    [Theory]
    [InlineData("\"red\"", true)]
    [InlineData("\"blue\"", false)]
    [InlineData("2", true)]   // numeric enum members compare by numeric value
    [InlineData("2.0", true)]
    public void Enum_Keyword_Is_Enforced(string instance, bool expectedValid)
    {
        var result = Validate(instance, "{ \"enum\": [\"red\", \"green\", 2] }");

        Assert.Equal(expectedValid, result.IsValid);
        if (!expectedValid)
        {
            Assert.Equal(SchemaMismatchKind.EnumViolation, Assert.Single(result.Issues).Kind);
        }
    }

    [Fact]
    public void Const_Keyword_Is_Enforced()
    {
        Assert.False(Validate("\"v2\"", "{ \"const\": \"v1\" }").IsValid);
        Assert.True(Validate("\"v1\"", "{ \"const\": \"v1\" }").IsValid);
    }

    [Theory]
    [InlineData("5", "{ \"type\": \"number\", \"minimum\": 1, \"maximum\": 10 }", true)]
    [InlineData("0", "{ \"type\": \"number\", \"minimum\": 1 }", false)]
    [InlineData("11", "{ \"type\": \"number\", \"maximum\": 10 }", false)]
    [InlineData("5", "{ \"type\": \"number\", \"exclusiveMinimum\": 5 }", false)]
    [InlineData("5", "{ \"type\": \"number\", \"exclusiveMaximum\": 5 }", false)]
    [InlineData("6", "{ \"type\": \"number\", \"multipleOf\": 3 }", true)]
    [InlineData("7", "{ \"type\": \"number\", \"multipleOf\": 3 }", false)]
    public void Numeric_Constraints_Are_Enforced(string instance, string schema, bool expectedValid)
    {
        Assert.Equal(expectedValid, Validate(instance, schema).IsValid);
    }

    [Theory]
    [InlineData("\"abc\"", "{ \"type\": \"string\", \"minLength\": 2, \"maxLength\": 4 }", true)]
    [InlineData("\"a\"", "{ \"type\": \"string\", \"minLength\": 2 }", false)]
    [InlineData("\"abcde\"", "{ \"type\": \"string\", \"maxLength\": 4 }", false)]
    [InlineData("\"task-1\"", "{ \"type\": \"string\", \"pattern\": \"^task-[0-9]+$\" }", true)]
    [InlineData("\"task-x\"", "{ \"type\": \"string\", \"pattern\": \"^task-[0-9]+$\" }", false)]
    public void String_Constraints_Are_Enforced(string instance, string schema, bool expectedValid)
    {
        Assert.Equal(expectedValid, Validate(instance, schema).IsValid);
    }

    [Theory]
    [InlineData("[1, 2]", "{ \"type\": \"array\", \"minItems\": 1, \"maxItems\": 3 }", true)]
    [InlineData("[]", "{ \"type\": \"array\", \"minItems\": 1 }", false)]
    [InlineData("[1, 2, 3, 4]", "{ \"type\": \"array\", \"maxItems\": 3 }", false)]
    [InlineData("[1, 2]", "{ \"type\": \"array\", \"uniqueItems\": true }", true)]
    [InlineData("[1, 1.0]", "{ \"type\": \"array\", \"uniqueItems\": true }", false)]
    [InlineData("[{\"a\":1}, {\"a\":1}]", "{ \"type\": \"array\", \"uniqueItems\": true }", false)]
    public void Array_Constraints_Are_Enforced(string instance, string schema, bool expectedValid)
    {
        Assert.Equal(expectedValid, Validate(instance, schema).IsValid);
    }

    [Fact]
    public void Nested_Item_Mismatches_Carry_Array_Index_Paths()
    {
        var result = Validate(
            "{ \"ids\": [\"ok\", 42] }",
            "{ \"type\": \"object\", \"properties\": { \"ids\": { \"type\": \"array\", \"items\": { \"type\": \"string\" } } } }");

        Assert.False(result.IsValid);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(SchemaMismatchKind.TypeMismatch, issue.Kind);
        Assert.Equal("$.ids[1]", issue.Path);
    }

    [Fact]
    public void Additional_Properties_Are_Allowed_When_Not_Forbidden()
    {
        var result = Validate(
            "{ \"name\": \"Ada\", \"extra\": 1 }",
            "{ \"type\": \"object\", \"properties\": { \"name\": { \"type\": \"string\" } } }");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Annotations_And_Format_Are_Ignored()
    {
        var result = Validate(
            "\"not-an-email\"",
            "{ \"type\": \"string\", \"format\": \"email\", \"title\": \"t\", \"description\": \"d\", \"default\": \"x\" }");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Boolean_Subschemas_Are_Honored()
    {
        var result = Validate(
            "{ \"a\": 1 }",
            "{ \"type\": \"object\", \"properties\": { \"a\": true, \"b\": false } }");

        Assert.True(result.IsValid);

        var failing = Validate(
            "{ \"b\": 1 }",
            "{ \"type\": \"object\", \"properties\": { \"a\": true, \"b\": false } }");

        Assert.False(failing.IsValid);
    }
}
