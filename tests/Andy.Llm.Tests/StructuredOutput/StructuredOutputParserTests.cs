using System.Text.Json;
using System.Text.Json.Nodes;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Llm.StructuredOutput;
using Xunit;

namespace Andy.Llm.Tests.StructuredOutput;

/// <summary>
/// Tests for <see cref="StructuredOutputParser"/> and the <see cref="StructuredOutputResult"/>
/// envelope (LLM-I103): every failure mode — refusal, truncation, malformed JSON, schema
/// mismatch, invalid schema, unsupported capability — is distinguishable from success.
/// </summary>
public class StructuredOutputParserTests
{
    private const string PersonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["name", "age"],
          "properties": {
            "name": { "type": "string", "minLength": 1 },
            "age": { "type": "integer", "minimum": 0 }
          }
        }
        """;

    [Fact]
    public void Success_Carries_Parsed_Value_And_Diagnostics()
    {
        var content = "{ \"name\": \"Ada\", \"age\": 36 }";

        var result = StructuredOutputParser.Parse(content, "stop", PersonSchema);

        Assert.True(result.IsSuccess);
        Assert.Equal(StructuredOutputStatus.Success, result.Status);
        Assert.Equal("Ada", result.Value!["name"]!.GetValue<string>());
        Assert.Equal(36, result.GetValue<Person>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Age);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(content, result.RawContent);
        Assert.Empty(result.SchemaIssues);
        Assert.NotNull(result.DiagnosticMessage);
    }

    private sealed record Person
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
    }

    [Fact]
    public void GetValue_Throws_For_Non_Success_Results()
    {
        var result = StructuredOutputParser.Parse("{", "stop", PersonSchema);

        Assert.Throws<InvalidOperationException>(() => result.GetValue<Person>());
    }

    [Fact]
    public void Explicit_Refusal_Reason_Produces_Refusal()
    {
        var result = StructuredOutputParser.Parse(
            null, "stop", PersonSchema, refusalReason: "Request violates the usage policy.");

        Assert.Equal(StructuredOutputStatus.Refusal, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal("Request violates the usage policy.", result.RefusalReason);
    }

    [Theory]
    [InlineData("content_filter")]
    [InlineData("refusal")]
    public void Refusal_Finish_Reasons_Produce_Refusal(string finishReason)
    {
        var result = StructuredOutputParser.Parse(null, finishReason, PersonSchema);

        Assert.Equal(StructuredOutputStatus.Refusal, result.Status);
        Assert.Equal(finishReason, result.FinishReason);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("max_tokens")]
    [InlineData("MAX_TOKENS")]
    public void Length_Finish_Reasons_Produce_Truncated(string finishReason)
    {
        // Even though the content is valid JSON, a length finish reason means the payload
        // cannot be trusted to be complete.
        var result = StructuredOutputParser.Parse("{ \"name\": \"Ada\", \"age\": 36 }", finishReason, PersonSchema);

        Assert.Equal(StructuredOutputStatus.Truncated, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(finishReason, result.FinishReason);
    }

    [Fact]
    public void Truncated_Payload_Is_Not_Reported_As_MalformedJson()
    {
        var result = StructuredOutputParser.Parse("{ \"name\": \"Ad", "length", PersonSchema);

        Assert.Equal(StructuredOutputStatus.Truncated, result.Status);
        Assert.NotEqual(StructuredOutputStatus.MalformedJson, result.Status);
    }

    [Theory]
    [InlineData("{ \"name\": \"Ada\", \"age\": }")]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("   ")]
    public void Unparseable_Content_Produces_MalformedJson(string content)
    {
        var result = StructuredOutputParser.Parse(content, "stop", PersonSchema);

        Assert.Equal(StructuredOutputStatus.MalformedJson, result.Status);
        Assert.NotNull(result.ParseErrorDetail);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Well_Formed_But_Non_Conforming_Content_Produces_SchemaMismatch()
    {
        var content = "{ \"name\": \"Ada\", \"age\": -3, \"unexpected\": true }";

        var result = StructuredOutputParser.Parse(content, "stop", PersonSchema);

        Assert.Equal(StructuredOutputStatus.SchemaMismatch, result.Status);
        Assert.NotNull(result.RawContent);
        Assert.Contains(result.SchemaIssues, i => i.Kind == SchemaMismatchKind.ConstraintViolation);
        Assert.Contains(result.SchemaIssues, i => i.Kind == SchemaMismatchKind.UnexpectedProperty);
    }

    [Fact]
    public void Missing_Required_Property_Produces_SchemaMismatch_Not_MalformedJson()
    {
        var result = StructuredOutputParser.Parse("{ \"name\": \"Ada\" }", "stop", PersonSchema);

        Assert.Equal(StructuredOutputStatus.SchemaMismatch, result.Status);
        var issue = Assert.Single(result.SchemaIssues);
        Assert.Equal(SchemaMismatchKind.MissingRequiredProperty, issue.Kind);
        Assert.Equal("$.age", issue.Path);
    }

    [Fact]
    public void Type_Mismatch_Produces_SchemaMismatch_With_Instance_Path()
    {
        var result = StructuredOutputParser.Parse("{ \"name\": \"Ada\", \"age\": \"thirty-six\" }", "stop", PersonSchema);

        Assert.Equal(StructuredOutputStatus.SchemaMismatch, result.Status);
        var issue = Assert.Single(result.SchemaIssues);
        Assert.Equal(SchemaMismatchKind.TypeMismatch, issue.Kind);
        Assert.Equal("$.age", issue.Path);
    }

    [Fact]
    public void Schema_Failing_Policy_Produces_InvalidSchema_With_Policy_Issues()
    {
        var result = StructuredOutputParser.Parse(
            "{ \"name\": \"Ada\", \"age\": 36 }", "stop", "{ \"patternProperties\": {} }");

        Assert.Equal(StructuredOutputStatus.InvalidSchema, result.Status);
        var issue = Assert.Single(result.PolicyIssues);
        Assert.Equal(JsonSchemaIssueKind.UnsupportedKeyword, issue.Kind);
    }

    [Fact]
    public void Unsupported_Capability_Is_Explicitly_Representable()
    {
        var result = StructuredOutputResult.Unsupported("json_schema response format", "provider supports text only");

        Assert.Equal(StructuredOutputStatus.UnsupportedCapability, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Contains("json_schema response format", result.DiagnosticMessage);
    }

    [Fact]
    public void All_Six_Statuses_Are_Distinguishable()
    {
        var results = new[]
        {
            StructuredOutputParser.Parse("{ \"name\": \"Ada\", \"age\": 36 }", "stop", PersonSchema),
            StructuredOutputParser.Parse(null, "stop", PersonSchema, refusalReason: "no"),
            StructuredOutputParser.Parse("{ }", "length", PersonSchema),
            StructuredOutputParser.Parse("{", "stop", PersonSchema),
            StructuredOutputParser.Parse("{ }", "stop", PersonSchema),
            StructuredOutputParser.Parse("{ }", "stop", "{ \"$ref\": \"#/x\" }"),
            StructuredOutputResult.Unsupported("strict mode"),
        };

        Assert.Equal(
            new[]
            {
                StructuredOutputStatus.Success,
                StructuredOutputStatus.Refusal,
                StructuredOutputStatus.Truncated,
                StructuredOutputStatus.MalformedJson,
                StructuredOutputStatus.SchemaMismatch,
                StructuredOutputStatus.InvalidSchema,
                StructuredOutputStatus.UnsupportedCapability,
            },
            results.Select(r => r.Status).ToArray());
    }

    [Fact]
    public void FromResponse_Reads_Content_And_FinishReason()
    {
        var response = new LlmResponse
        {
            AssistantMessage = new Message { Role = Role.Assistant, Content = "{ \"name\": \"Ada\", \"age\": 36 }" },
            FinishReason = "stop",
        };

        var result = StructuredOutputParser.FromResponse(response, PersonSchema);

        Assert.True(result.IsSuccess);
        Assert.Equal("stop", result.FinishReason);
    }

    [Fact]
    public void FromResponse_Maps_Length_FinishReason_To_Truncated()
    {
        var response = new LlmResponse
        {
            AssistantMessage = new Message { Role = Role.Assistant, Content = "{ \"name\": \"Ada\"" },
            FinishReason = "length",
        };

        var result = StructuredOutputParser.FromResponse(response, PersonSchema);

        Assert.Equal(StructuredOutputStatus.Truncated, result.Status);
    }

    [Fact]
    public void Value_Is_A_Mutable_Free_JsonNode_Copy()
    {
        var result = StructuredOutputParser.Parse("{ \"name\": \"Ada\", \"age\": 36 }", "stop", PersonSchema);

        var node = Assert.IsType<JsonObject>(result.Value);
        Assert.Equal(36, node["age"]!.GetValue<int>());
    }
}
