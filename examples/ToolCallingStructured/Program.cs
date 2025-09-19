using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm;
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace ToolCallingStructured;

/// <summary>
/// Example demonstrating structured tool calling capabilities
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        // Run examples
        var examples = new ToolCallingExamples(loggerFactory);

        Console.WriteLine("=== Structured Tool Calling Examples ===\n");
        await examples.SimpleToolCallExample();
        await examples.MultipleToolsExample();
        await examples.ToolWithSchemaValidation();
        await examples.ToolExecutionPipeline();
        await examples.ParallelToolExecution();
    }
}

public class ToolCallingExamples
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ToolCallingExamples> _logger;
    private readonly StructuredResponseFactory _responseFactory;

    public ToolCallingExamples(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ToolCallingExamples>();
        _responseFactory = new StructuredResponseFactory(
            _loggerFactory.CreateLogger<StructuredResponseFactory>()
        );
    }

    /// <summary>
    /// Simple tool call with structured response
    /// </summary>
    public async Task SimpleToolCallExample()
    {
        Console.WriteLine("1. Simple Tool Call Example");
        Console.WriteLine("-------------------");

        // Define tool with schema
        var weatherTool = new ExampleToolDeclaration
        {
            Name = "get_weather",
            Description = "Get current weather for a location",
            Parameters = new ExampleToolParameters
            {
                Type = "object",
                Properties = new Dictionary<string, ExampleParameterSchema>
                {
                    ["location"] = new ExampleParameterSchema
                    {
                        Type = "string",
                        Description = "City and state, e.g., San Francisco, CA"
                    },
                    ["units"] = new ExampleParameterSchema
                    {
                        Type = "string",
                        Enum = new[] { "celsius", "fahrenheit" },
                        Description = "Temperature units"
                    }
                },
                Required = new[] { "location" }
            }
        };

        // Simulate LLM response with tool call
        var llmResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""I'll check the weather for you."",
                    ""tool_calls"": [{
                        ""id"": ""call_weather_123"",
                        ""type"": ""function"",
                        ""function"": {
                            ""name"": ""get_weather"",
                            ""arguments"": ""{\""location\"": \""San Francisco, CA\"", \""units\"": \""fahrenheit\""}""
                        }
                    }]
                }
            }]
        }";

        var response = _responseFactory.CreateFromOpenAI(llmResponse);

        Console.WriteLine("LLM Response:");
        Console.WriteLine($"- Message: {response.TextContent}");

        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"- Tool Call: {toolCall.Name}");
            Console.WriteLine($"  ID: {toolCall.Id}");

            if (toolCall.ParseError == null && toolCall.Arguments != null)
            {
                Console.WriteLine("  Arguments:");
                foreach (var arg in toolCall.Arguments)
                {
                    Console.WriteLine($"    - {arg.Key}: {arg.Value}");
                }

                // Execute the tool
                Console.WriteLine("\nExecuting tool...");
                var result = await ExecuteTool(toolCall.Name, toolCall.Arguments);
                Console.WriteLine($"Result: {JsonSerializer.Serialize(result)}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Multiple tools in one request
    /// </summary>
    public async Task MultipleToolsExample()
    {
        Console.WriteLine("2. Multiple Tools Example");
        Console.WriteLine("------------------------");

        var tools = new List<ExampleToolDeclaration>
        {
            new ExampleToolDeclaration
            {
                Name = "search_web",
                Description = "Search the web for information",
                Parameters = new ExampleToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ExampleParameterSchema>
                    {
                        ["query"] = new ExampleParameterSchema { Type = "string" },
                        ["num_results"] = new ExampleParameterSchema { Type = "integer", Default = 5 }
                    },
                    Required = new[] { "query" }
                }
            },
            new ExampleToolDeclaration
            {
                Name = "calculate",
                Description = "Perform mathematical calculations",
                Parameters = new ExampleToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ExampleParameterSchema>
                    {
                        ["expression"] = new ExampleParameterSchema { Type = "string" }
                    },
                    Required = new[] { "expression" }
                }
            }
        };

        // Simulated response with multiple tool calls
        var llmResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""I'll search for Tokyo's population and then calculate 10% of it."",
                    ""tool_calls"": [
                        {
                            ""id"": ""call_search_1"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""search_web"",
                                ""arguments"": ""{\""query\"": \""Tokyo population 2024\"", \""num_results\"": 3}""
                            }
                        },
                        {
                            ""id"": ""call_calc_1"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""calculate"",
                                ""arguments"": ""{\""expression\"": \""14000000 * 0.1\""}""
                            }
                        }
                    ]
                }
            }]
        }";

        var response = _responseFactory.CreateFromOpenAI(llmResponse);

        Console.WriteLine($"Model Response: {response.TextContent}");
        Console.WriteLine($"Number of tool calls: {response.ToolCalls.Count}");

        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"\nTool: {toolCall.Name} (ID: {toolCall.Id})");

            if (toolCall.Arguments != null)
            {
                var result = await ExecuteTool(toolCall.Name, toolCall.Arguments);
                Console.WriteLine($"Result: {JsonSerializer.Serialize(result)}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Tool call with schema validation
    /// </summary>
    public async Task ToolWithSchemaValidation()
    {
        Console.WriteLine("3. Tool with Schema Validation");
        Console.WriteLine("------------------------------");

        // Tool call with invalid arguments
        var invalidResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""tool_calls"": [{
                        ""id"": ""call_invalid"",
                        ""function"": {
                            ""name"": ""get_weather"",
                            ""arguments"": ""{ invalid json }""
                        }
                    }]
                }
            }]
        }";

        var response = _responseFactory.CreateFromOpenAI(invalidResponse);

        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"Tool: {toolCall.Name}");

            if (toolCall.ParseError != null)
            {
                Console.WriteLine($"⚠️ Parse Error: {toolCall.ParseError.Message}");
                Console.WriteLine($"Raw Arguments: {toolCall.ArgumentsJson}");
                Console.WriteLine("Attempting fallback processing...");

                // Demonstrate fallback handling
                var fallbackResult = await HandleInvalidToolCall(toolCall);
                Console.WriteLine($"Fallback Result: {JsonSerializer.Serialize(fallbackResult)}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Tool execution pipeline with structured responses
    /// </summary>
    public async Task ToolExecutionPipeline()
    {
        Console.WriteLine("4. Tool Execution Pipeline");
        Console.WriteLine("-------------------------");

        var pipeline = new ToolExecutionPipeline(_logger);

        // Create structured tool calls
        var toolCalls = new List<StructuredToolCall>
        {
            new StructuredToolCall
            {
                Id = "call_1",
                Name = "get_time",
                Arguments = new Dictionary<string, object> { ["timezone"] = "UTC" }
            },
            new StructuredToolCall
            {
                Id = "call_2",
                Name = "get_date",
                Arguments = new Dictionary<string, object> { ["format"] = "ISO8601" }
            }
        };

        var results = await pipeline.ExecuteToolCalls(toolCalls);

        foreach (var result in results)
        {
            Console.WriteLine($"Tool {result.CallId}: {result.Status}");
            if (result.Data != null)
            {
                Console.WriteLine($"  Result: {JsonSerializer.Serialize(result.Data)}");
            }
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Parallel tool execution
    /// </summary>
    public async Task ParallelToolExecution()
    {
        Console.WriteLine("5. Parallel Tool Execution");
        Console.WriteLine("-------------------------");

        var toolCalls = new List<StructuredToolCall>
        {
            new StructuredToolCall { Id = "parallel_1", Name = "slow_operation_1" },
            new StructuredToolCall { Id = "parallel_2", Name = "slow_operation_2" },
            new StructuredToolCall { Id = "parallel_3", Name = "slow_operation_3" }
        };

        Console.WriteLine($"Executing {toolCalls.Count} tools in parallel...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var tasks = toolCalls.Select(async call =>
        {
            await Task.Delay(1000); // Simulate slow operation
            return new StructuredToolResult
            {
                CallId = call.Id,
                Status = "completed",
                Data = new { executionTime = "1s" }
            };
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        Console.WriteLine($"All tools completed in {stopwatch.ElapsedMilliseconds}ms");

        foreach (var result in results)
        {
            Console.WriteLine($"  - {result.CallId}: {result.Status}");
        }

        Console.WriteLine();
    }

    // Helper methods
    private async Task<object> ExecuteTool(string name, Dictionary<string, object?> arguments)
    {
        await Task.Delay(100); // Simulate execution

        return name switch
        {
            "get_weather" => new { temperature = 72, condition = "sunny" },
            "search_web" => new { results = new[] { "Result 1", "Result 2" } },
            "calculate" => new { result = 1400000 },
            _ => new { error = "Unknown tool" }
        };
    }

    private async Task<object> HandleInvalidToolCall(Andy.Llm.Parsing.StructuredToolCall toolCall)
    {
        await Task.Delay(50);
        return new { error = "Invalid arguments", toolName = toolCall.Name };
    }
}

// Supporting classes for structured tool calling
public class ExampleToolDeclaration
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ExampleToolParameters? Parameters { get; set; }
}

public class ExampleToolParameters
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ExampleParameterSchema> Properties { get; set; } = new();
    public string[]? Required { get; set; }
}

public class ExampleParameterSchema
{
    public string Type { get; set; } = "";
    public string? Description { get; set; }
    public object? Default { get; set; }
    public string[]? Enum { get; set; }
}

public class ToolExecutionPipeline
{
    private readonly ILogger _logger;

    public ToolExecutionPipeline(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<List<StructuredToolResult>> ExecuteToolCalls(List<StructuredToolCall> toolCalls)
    {
        var results = new List<StructuredToolResult>();

        foreach (var toolCall in toolCalls)
        {
            _logger.LogInformation($"Executing tool: {toolCall.Name}");

            var result = new StructuredToolResult
            {
                CallId = toolCall.Id,
                Status = "completed",
                Data = new { timestamp = DateTime.UtcNow }
            };

            results.Add(result);
            await Task.Delay(50); // Simulate execution
        }

        return results;
    }
}

public class StructuredToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, object>? Arguments { get; set; }
    public string? ArgumentsJson { get; set; }
    public ParseError? ParseError { get; set; }
}

public class StructuredToolResult
{
    public string CallId { get; set; } = "";
    public string Status { get; set; } = "";
    public object? Data { get; set; }
    public string? Error { get; set; }
}

public class ParseError
{
    public string Message { get; set; } = "";
    public int? Line { get; set; }
    public int? Column { get; set; }
}