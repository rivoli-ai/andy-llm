using System;
using System.Linq;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Andy.Llm.Tests;

public class AndyModelTest
{
    private readonly ITestOutputHelper _output;

    public AndyModelTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ShowAndyModelTypes()
    {
        var assembly = Assembly.Load("Andy.Model");
        var types = assembly.GetExportedTypes();

        _output.WriteLine($"Found {types.Length} exported types in Andy.Model:");
        foreach (var type in types.OrderBy(t => t.FullName))
        {
            _output.WriteLine($"  - {type.FullName}");
        }

        // Also check for specific types we need
        var typeNames = types.Select(t => t.Name).ToList();
        _output.WriteLine("\nChecking for expected types:");
        var expectedTypes = new[] { "LlmRequest", "LlmResponse", "Message", "ToolCall", "ToolDeclaration", "ModelInfo" };
        foreach (var expectedType in expectedTypes)
        {
            var found = typeNames.Contains(expectedType);
            _output.WriteLine($"  {expectedType}: {(found ? "✓ FOUND" : "✗ NOT FOUND")}");
        }
    }
}