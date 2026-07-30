using System.Reflection;

namespace Andy.Llm.StructuredOutput;

/// <summary>
/// The canonical structured-output JSON Schemas for the Andy Analyst use cases, shipped as
/// embedded resources so both the library and its consumers validate against the exact same
/// documents: an analysis plan with task dependencies, a bounded child-task result, a
/// classification result, and an extraction result. Each schema stays within the supported
/// subset enforced by <see cref="JsonSchemaPolicy"/>.
/// </summary>
public static class CanonicalSchemas
{
    private const string ResourcePrefix = "Andy.Llm.StructuredOutput.Schemas.";

    private static readonly Lazy<string> AnalysisPlanSchema = Load("analysis-plan.schema.json");
    private static readonly Lazy<string> ChildTaskResultSchema = Load("child-task-result.schema.json");
    private static readonly Lazy<string> ClassificationResultSchema = Load("classification-result.schema.json");
    private static readonly Lazy<string> ExtractionResultSchema = Load("extraction-result.schema.json");

    /// <summary>A research-analysis plan decomposed into tasks with explicit dependencies.</summary>
    public static string AnalysisPlan => AnalysisPlanSchema.Value;

    /// <summary>The bounded answer produced by executing a single child task of a plan.</summary>
    public static string ChildTaskResult => ChildTaskResultSchema.Value;

    /// <summary>A single-label classification with confidence and rationale.</summary>
    public static string ClassificationResult => ClassificationResultSchema.Value;

    /// <summary>Entities and values extracted from source text.</summary>
    public static string ExtractionResult => ExtractionResultSchema.Value;

    private static Lazy<string> Load(string fileName) => new(() =>
    {
        var assembly = typeof(CanonicalSchemas).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource '{ResourcePrefix + fileName}' is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
