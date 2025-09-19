using System;
using System.Threading.Tasks;
using Andy.Llm.Parsing;
using Microsoft.Extensions.Logging;

namespace QuickStart;

/// <summary>
/// Quick start example showing how to run the structured output and parsing examples
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Andy.Llm Quick Start Examples ===\n");
        Console.WriteLine("These examples demonstrate the structured output and hybrid parsing features.\n");
        
        Console.WriteLine("To run the examples, you need to:");
        Console.WriteLine("1. Set up your API keys as environment variables");
        Console.WriteLine("2. Navigate to the example directory");
        Console.WriteLine("3. Run the example\n");
        
        Console.WriteLine("Available Examples:");
        Console.WriteLine("==================\n");
        
        Console.WriteLine("1. STRUCTURED OUTPUT EXAMPLE");
        Console.WriteLine("   Shows JSON Schema validation and structured responses");
        Console.WriteLine("   Run with: dotnet run --project examples/StructuredOutput\n");
        
        Console.WriteLine("2. HYBRID PARSING EXAMPLE");
        Console.WriteLine("   Demonstrates parsing of OpenAI and other provider responses");
        Console.WriteLine("   Run with: dotnet run --project examples/HybridParsing\n");
        
        Console.WriteLine("3. TOOL CALLING EXAMPLE");
        Console.WriteLine("   Advanced tool/function calling with schema validation");
        Console.WriteLine("   Run with: dotnet run --project examples/ToolCallingStructured\n");
        
        Console.WriteLine("\nEnvironment Setup:");
        Console.WriteLine("==================");
        Console.WriteLine("Set your API keys before running:");
        Console.WriteLine("  export OPENAI_API_KEY=\"your-key\"");
        Console.WriteLine("  export CEREBRAS_API_KEY=\"your-key\"");
        Console.WriteLine("  export OLLAMA_API_BASE=\"http://localhost:11434\"");
        Console.WriteLine("  export AZURE_OPENAI_KEY=\"your-key\"");
        Console.WriteLine("  export AZURE_OPENAI_ENDPOINT=\"your-endpoint\"\n");

        Console.WriteLine("You can also set the provider:");
        Console.WriteLine("  export LLM_PROVIDER=openai");
        Console.WriteLine("  export LLM_PROVIDER=cerebras");
        Console.WriteLine("  export LLM_PROVIDER=ollama");
        Console.WriteLine("  export LLM_PROVIDER=azure\n");
        
        Console.WriteLine("Example Commands:");
        Console.WriteLine("================");
        Console.WriteLine("# Run with default provider");
        Console.WriteLine("dotnet run --project examples/StructuredOutput\n");
        
        Console.WriteLine("# Run with specific provider");
        Console.WriteLine("LLM_PROVIDER=openai dotnet run --project examples/HybridParsing\n");
        
        Console.WriteLine("# Build all examples first");
        Console.WriteLine("dotnet build");
        Console.WriteLine("dotnet run --project examples/ToolCallingStructured\n");
        
        // Show a simple parsing demo
        Console.WriteLine("Quick Demo - Parsing Different Response Formats:");
        Console.WriteLine("=================================================\n");

        var factory = new StructuredResponseFactory();

        // OpenAI format
        var openAiResponse = @"{""choices"":[{""message"":{""content"":""Hello from OpenAI!""}}]}";
        var parsed1 = factory.CreateFromOpenAI(openAiResponse);
        Console.WriteLine($"OpenAI Format Response: {parsed1.TextContent}");

        // Generic/text format
        var textResponse = "Hello from a text-based response!";
        var parsed2 = factory.CreateFromGeneric(textResponse, null);
        Console.WriteLine($"Text Format Response: {parsed2.TextContent}");
        
        Console.WriteLine("\nFor more detailed examples, run the individual example projects listed above.");
    }
}