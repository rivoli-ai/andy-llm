using Andy.Llm.StructuredOutput;
using Xunit;

namespace Andy.Llm.Tests.StructuredOutput;

/// <summary>
/// Tests for the canonical structured-output schemas (LLM-I105): every fixture passes
/// <see cref="JsonSchemaPolicy"/> screening, a conforming document parses to
/// <see cref="StructuredOutputStatus.Success"/>, and non-conforming documents fail with
/// <see cref="StructuredOutputStatus.SchemaMismatch"/> — never
/// <see cref="StructuredOutputStatus.MalformedJson"/>.
/// </summary>
public class CanonicalSchemaFixtureTests
{
    public static IEnumerable<object[]> AllSchemas()
    {
        yield return new object[] { "AnalysisPlan", CanonicalSchemas.AnalysisPlan };
        yield return new object[] { "ChildTaskResult", CanonicalSchemas.ChildTaskResult };
        yield return new object[] { "ClassificationResult", CanonicalSchemas.ClassificationResult };
        yield return new object[] { "ExtractionResult", CanonicalSchemas.ExtractionResult };
    }

    [Theory]
    [MemberData(nameof(AllSchemas))]
    public void Each_Fixture_Passes_Policy_Validation(string name, string schema)
    {
        var result = JsonSchemaPolicy.Validate(schema);

        Assert.True(result.IsValid, $"{name} failed policy screening: {string.Join("; ", result.Issues.Select(i => i.ToString()))}");
    }

    // ---- Conforming documents: one per fixture, exercising the success path ----

    public static IEnumerable<object[]> ConformingDocuments()
    {
        yield return new object[]
        {
            "AnalysisPlan", CanonicalSchemas.AnalysisPlan,
            """
            {
              "goal": "Assess the impact of rate changes on European bank equities",
              "assumptions": ["Public filings are sufficient; no paid data sources."],
              "tasks": [
                {
                  "id": "t1",
                  "title": "Collect recent rate decisions",
                  "instructions": "Gather the last four ECB and BoE rate decisions with dates.",
                  "dependsOn": [],
                  "toolHint": "search",
                  "outputSchema": { "type": "object", "properties": { "decisions": { "type": "array" } } }
                },
                {
                  "id": "t2",
                  "title": "Estimate sensitivity",
                  "instructions": "Estimate net-interest-income sensitivity for the top five banks.",
                  "dependsOn": ["t1"],
                  "toolHint": "analyze"
                }
              ]
            }
            """,
        };
        yield return new object[]
        {
            "ChildTaskResult", CanonicalSchemas.ChildTaskResult,
            """
            {
              "taskId": "t2",
              "status": "completed",
              "answer": "A 25 bps cut compresses NII for the sampled banks by 1-3% over 12 months.",
              "confidence": 0.72,
              "sources": [
                { "title": "ECB press release", "uri": "https://www.ecb.europa.eu/example" },
                { "title": "Bank annual report" }
              ],
              "tokensUsed": 1843
            }
            """,
        };
        yield return new object[]
        {
            "ClassificationResult", CanonicalSchemas.ClassificationResult,
            """
            {
              "category": "analytical",
              "confidence": 0.9,
              "rationale": "The question requires synthesizing multiple data points, not a single fact lookup.",
              "secondaryCategories": ["comparative"]
            }
            """,
        };
        yield return new object[]
        {
            "ExtractionResult", CanonicalSchemas.ExtractionResult,
            """
            {
              "entities": [
                { "name": "ECB", "kind": "organization", "value": "European Central Bank" },
                { "name": "deposit facility rate", "kind": "percentage", "value": 3.25, "unit": "%", "confidence": 0.98 }
              ],
              "warnings": ["Rate effective date not stated in the excerpt."]
            }
            """,
        };
    }

    [Theory]
    [MemberData(nameof(ConformingDocuments))]
    public void Conforming_Document_Yields_Success(string name, string schema, string document)
    {
        var result = StructuredOutputParser.Parse(document, "stop", schema);

        Assert.True(result.IsSuccess, $"{name} conforming document failed: {result.DiagnosticMessage}");
        Assert.Equal(StructuredOutputStatus.Success, result.Status);
        Assert.NotNull(result.Value);
    }

    // ---- Non-conforming documents: two per fixture, all well-formed JSON ----

    public static IEnumerable<object[]> NonConformingDocuments()
    {
        yield return new object[]
        {
            "AnalysisPlan: task missing dependsOn",
            CanonicalSchemas.AnalysisPlan,
            """
            {
              "goal": "Analyze EV market share",
              "tasks": [
                { "id": "t1", "title": "Collect sales data", "instructions": "Gather 2024 sales by manufacturer." }
              ]
            }
            """,
        };
        yield return new object[]
        {
            "AnalysisPlan: unknown toolHint",
            CanonicalSchemas.AnalysisPlan,
            """
            {
              "goal": "Analyze EV market share",
              "tasks": [
                { "id": "t1", "title": "Collect sales data", "instructions": "Gather 2024 sales.", "dependsOn": [], "toolHint": "teleport" }
              ]
            }
            """,
        };
        yield return new object[]
        {
            "ChildTaskResult: confidence above 1",
            CanonicalSchemas.ChildTaskResult,
            """
            { "taskId": "t1", "status": "completed", "answer": "42%", "confidence": 1.4 }
            """,
        };
        yield return new object[]
        {
            "ChildTaskResult: invalid status",
            CanonicalSchemas.ChildTaskResult,
            """
            { "taskId": "t1", "status": "half-done", "answer": "42%" }
            """,
        };
        yield return new object[]
        {
            "ClassificationResult: missing rationale",
            CanonicalSchemas.ClassificationResult,
            """
            { "category": "factual", "confidence": 0.5 }
            """,
        };
        yield return new object[]
        {
            "ClassificationResult: extra property",
            CanonicalSchemas.ClassificationResult,
            """
            { "category": "factual", "confidence": 0.5, "rationale": "Direct lookup.", "model": "gpt-4o" }
            """,
        };
        yield return new object[]
        {
            "ExtractionResult: entity missing value",
            CanonicalSchemas.ExtractionResult,
            """
            { "entities": [ { "name": "ECB", "kind": "organization" } ] }
            """,
        };
        yield return new object[]
        {
            "ExtractionResult: wrong kind enum and object value",
            CanonicalSchemas.ExtractionResult,
            """
            { "entities": [ { "name": "ECB", "kind": "concept", "value": { "nested": true } } ] }
            """,
        };
    }

    [Theory]
    [MemberData(nameof(NonConformingDocuments))]
    public void Non_Conforming_Document_Yields_SchemaMismatch_Not_MalformedJson(
        string name, string schema, string document)
    {
        var result = StructuredOutputParser.Parse(document, "stop", schema);

        Assert.True(result.Status == StructuredOutputStatus.SchemaMismatch,
            $"{name}: expected SchemaMismatch, got {result.Status} ({result.DiagnosticMessage})");
        Assert.NotEmpty(result.SchemaIssues);
        Assert.NotNull(result.DiagnosticMessage);
        // Sanity: the failure is genuinely a conformance failure, not a parse failure.
        Assert.Null(result.ParseErrorDetail);
    }

    [Fact]
    public void Fixtures_Are_Loaded_From_Embedded_Resources()
    {
        // All four accessors return distinct, non-empty schema documents.
        var schemas = new[]
        {
            CanonicalSchemas.AnalysisPlan,
            CanonicalSchemas.ChildTaskResult,
            CanonicalSchemas.ClassificationResult,
            CanonicalSchemas.ExtractionResult,
        };

        Assert.All(schemas, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(4, schemas.Distinct().Count());
    }
}
