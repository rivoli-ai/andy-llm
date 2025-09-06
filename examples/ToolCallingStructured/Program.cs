using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Andy.Llm.Models;
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ToolCallingStructured;

/// <summary>
/// Example demonstrating tool/function calling with structured outputs
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

        var examples = new ToolCallingExamples(loggerFactory);
        
        Console.WriteLine("=== Tool Calling with Structured Outputs ===\n");
        
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
    private readonly StructuredResponseFactory _responseFactory;
    private readonly Dictionary<string, Func<Dictionary<string, object?>, Task<object>>> _toolRegistry;

    public ToolCallingExamples(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _responseFactory = new StructuredResponseFactory(
            loggerFactory.CreateLogger<StructuredResponseFactory>()
        );
        
        // Register available tools
        _toolRegistry = new Dictionary<string, Func<Dictionary<string, object?>, Task<object>>>
        {
            ["get_weather"] = GetWeatherTool,
            ["calculate"] = CalculateTool,
            ["search_web"] = SearchWebTool,
            ["send_email"] = SendEmailTool,
            ["query_database"] = QueryDatabaseTool
        };
    }

    /// <summary>
    /// Simple tool call example
    /// </summary>
    public async Task SimpleToolCallExample()
    {
        Console.WriteLine("1. Simple Tool Call");
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

        // Request with tool choice strategy
        // NOTE: In a real application, you'd convert ExampleToolDeclaration to Andy.Llm.Models.ToolDeclaration
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Search for the population of Tokyo and calculate 10% of it")
            },
            // Tools = tools, // Commented out as 'tools' is List<ExampleToolDeclaration>
            ToolChoice = ToolChoice.Auto, // Let the model decide which tools to use
            ResponseFormat = ResponseFormat.ToolCalls
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
        
        Console.WriteLine("Tool Execution Plan:");
        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"→ {toolCall.Name} (ID: {toolCall.Id})");
        }
        
        Console.WriteLine("\nExecuting tools sequentially:");
        var results = new List<StructuredToolResult>();
        
        foreach (var toolCall in response.ToolCalls)
        {
            if (toolCall.Arguments != null)
            {
                try
                {
                    var result = await ExecuteTool(toolCall.Name, toolCall.Arguments);
                    results.Add(new StructuredToolResult
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Result = result,
                        IsSuccess = true
                    });
                    Console.WriteLine($"✅ {toolCall.Name}: Success");
                }
                catch (Exception ex)
                {
                    results.Add(new StructuredToolResult
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        IsSuccess = false,
                        ErrorMessage = ex.Message
                    });
                    Console.WriteLine($"❌ {toolCall.Name}: {ex.Message}");
                }
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Tool with complex schema validation
    /// </summary>
    public async Task ToolWithSchemaValidation()
    {
        Console.WriteLine("3. Tool with Schema Validation");
        Console.WriteLine("------------------------------");

        var emailTool = new ExampleToolDeclaration
        {
            Name = "send_email",
            Description = "Send an email with validation",
            Parameters = new ExampleToolParameters
            {
                Type = "object",
                Properties = new Dictionary<string, ExampleParameterSchema>
                {
                    ["to"] = new ExampleParameterSchema
                    {
                        Type = "array",
                        Items = new ExampleParameterSchema
                        {
                            Type = "string",
                            Format = "email"
                        },
                        MinItems = 1,
                        MaxItems = 10
                    },
                    ["subject"] = new ExampleParameterSchema
                    {
                        Type = "string",
                        MinLength = 1,
                        MaxLength = 200
                    },
                    ["body"] = new ExampleParameterSchema
                    {
                        Type = "string",
                        MaxLength = 10000
                    },
                    ["priority"] = new ExampleParameterSchema
                    {
                        Type = "string",
                        Enum = new[] { "low", "normal", "high", "urgent" },
                        Default = "normal"
                    },
                    ["attachments"] = new ExampleParameterSchema
                    {
                        Type = "array",
                        Items = new ExampleParameterSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, ExampleParameterSchema>
                            {
                                ["filename"] = new ExampleParameterSchema { Type = "string" },
                                ["size"] = new ExampleParameterSchema { Type = "integer", Maximum = 25000000 }
                            }
                        }
                    }
                },
                Required = new[] { "to", "subject", "body" }
            }
        };

        // Example with validation
        var validCall = @"{
            ""id"": ""call_email_1"",
            ""type"": ""function"",
            ""function"": {
                ""name"": ""send_email"",
                ""arguments"": ""{\""to\"": [\""user@example.com\""], \""subject\"": \""Test Email\"", \""body\"": \""This is a test message.\"", \""priority\"": \""high\""}""
            }
        }";

        var invalidCall = @"{
            ""id"": ""call_email_2"",
            ""type"": ""function"",
            ""function"": {
                ""name"": ""send_email"",
                ""arguments"": ""{\""to\"": [], \""subject\"": \""\"", \""priority\"": \""invalid\""}""
            }
        }";

        Console.WriteLine("Valid Tool Call:");
        var validToolCall = ParseSingleToolCall(validCall);
        await ValidateAndExecuteTool(validToolCall, emailTool);
        
        Console.WriteLine("\nInvalid Tool Call:");
        var invalidToolCall = ParseSingleToolCall(invalidCall);
        await ValidateAndExecuteTool(invalidToolCall, emailTool);
        
        Console.WriteLine();
    }

    /// <summary>
    /// Complete tool execution pipeline
    /// </summary>
    public async Task ToolExecutionPipeline()
    {
        Console.WriteLine("4. Tool Execution Pipeline");
        Console.WriteLine("-------------------------");

        // Simulate a multi-turn conversation with tool calls
        var pipeline = new ToolExecutionPipeline(_toolRegistry);
        
        var conversation = new List<(string role, string content)>
        {
            ("user", "What's the weather in Paris and New York?"),
            ("assistant", @"{""tool_calls"": [{""id"": ""1"", ""function"": {""name"": ""get_weather"", ""arguments"": ""{\""location\"": \""Paris, France\""}""}}]}"),
            ("tool", @"{""temperature"": 18, ""condition"": ""Cloudy""}"),
            ("assistant", @"{""tool_calls"": [{""id"": ""2"", ""function"": {""name"": ""get_weather"", ""arguments"": ""{\""location\"": \""New York, NY\""}""}}]}"),
            ("tool", @"{""temperature"": 72, ""condition"": ""Sunny""}"),
            ("assistant", "The weather in Paris is 18°C and cloudy, while New York is 72°F and sunny.")
        };

        Console.WriteLine("Conversation Flow:");
        foreach (var (role, content) in conversation)
        {
            Console.WriteLine($"{role}: {(content.Length > 100 ? content.Substring(0, 97) + "..." : content)}");
            
            if (role == "assistant" && content.Contains("tool_calls"))
            {
                // Parse and execute tool calls
                var response = _responseFactory.CreateFromOpenAI($@"{{""choices"":[{{""message"":{{""content"":"""", {content.TrimStart('{')}}}]}}");
                
                foreach (var toolCall in response.ToolCalls)
                {
                    Console.WriteLine($"  → Executing: {toolCall.Name}");
                }
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Parallel tool execution for performance
    /// </summary>
    public async Task ParallelToolExecution()
    {
        Console.WriteLine("5. Parallel Tool Execution");
        Console.WriteLine("-------------------------");

        // Multiple independent tool calls that can run in parallel
        var toolCalls = new List<StructuredToolCall>
        {
            StructuredArgumentParser.CreateToolCall("call_1", "search_web", 
                @"{""query"": ""latest tech news""}"),
            StructuredArgumentParser.CreateToolCall("call_2", "get_weather", 
                @"{""location"": ""London, UK""}"),
            StructuredArgumentParser.CreateToolCall("call_3", "calculate", 
                @"{""expression"": ""sqrt(144) + pi * 2""}"),
            StructuredArgumentParser.CreateToolCall("call_4", "query_database", 
                @"{""query"": ""SELECT COUNT(*) FROM users""}"),
        };

        Console.WriteLine($"Executing {toolCalls.Count} tools in parallel...");
        var startTime = DateTime.UtcNow;
        
        // Execute all tools in parallel
        var tasks = toolCalls
            .Where(tc => tc.Arguments != null)
            .Select(async tc =>
            {
                try
                {
                    var result = await ExecuteTool(tc.Name, tc.Arguments!);
                    return new StructuredToolResult
                    {
                        CallId = tc.Id,
                        ToolName = tc.Name,
                        Result = result,
                        IsSuccess = true
                    };
                }
                catch (Exception ex)
                {
                    return new StructuredToolResult
                    {
                        CallId = tc.Id,
                        ToolName = tc.Name,
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        ExecutionError = ex
                    };
                }
            });

        var results = await Task.WhenAll(tasks);
        var duration = DateTime.UtcNow - startTime;
        
        Console.WriteLine($"Completed in {duration.TotalMilliseconds:F0}ms\n");
        
        Console.WriteLine("Results:");
        foreach (var result in results)
        {
            var status = result.IsSuccess ? "✅" : "❌";
            var message = result.IsSuccess 
                ? "Success" 
                : $"Failed: {result.ErrorMessage}";
            Console.WriteLine($"{status} {result.ToolName}: {message}");
        }
        Console.WriteLine();
    }

    // Tool implementations
    private async Task<object> GetWeatherTool(Dictionary<string, object?> args)
    {
        await Task.Delay(100); // Simulate API call
        var location = args["location"]?.ToString() ?? "Unknown";
        var units = args.ContainsKey("units") ? args["units"]?.ToString() : "celsius";
        
        return new
        {
            location,
            temperature = Random.Shared.Next(10, 30),
            units,
            condition = new[] { "Sunny", "Cloudy", "Rainy", "Partly Cloudy" }[Random.Shared.Next(4)],
            humidity = Random.Shared.Next(30, 80),
            wind_speed = Random.Shared.Next(5, 25)
        };
    }

    private async Task<object> CalculateTool(Dictionary<string, object?> args)
    {
        await Task.Delay(50); // Simulate calculation
        var expression = args["expression"]?.ToString() ?? "0";
        
        // In a real implementation, use a safe expression evaluator
        return new
        {
            expression,
            result = 42.0, // Placeholder
            steps = new[] { "Parse expression", "Evaluate", "Return result" }
        };
    }

    private async Task<object> SearchWebTool(Dictionary<string, object?> args)
    {
        await Task.Delay(200); // Simulate web search
        var query = args["query"]?.ToString() ?? "";
        var numResults = args.ContainsKey("num_results") ? Convert.ToInt32(args["num_results"]) : 5;
        
        return new
        {
            query,
            results = Enumerable.Range(1, numResults).Select(i => new
            {
                title = $"Result {i} for: {query}",
                url = $"https://example.com/result{i}",
                snippet = $"This is a snippet for result {i}..."
            }).ToArray()
        };
    }

    private async Task<object> SendEmailTool(Dictionary<string, object?> args)
    {
        await Task.Delay(150); // Simulate email sending
        return new
        {
            message_id = Guid.NewGuid().ToString(),
            status = "sent",
            timestamp = DateTime.UtcNow
        };
    }

    private async Task<object> QueryDatabaseTool(Dictionary<string, object?> args)
    {
        await Task.Delay(100); // Simulate database query
        return new
        {
            query = args["query"],
            rows_affected = 0,
            result = new[] { new { count = 42 } }
        };
    }

    private async Task<object?> ExecuteTool(string toolName, Dictionary<string, object?> arguments)
    {
        if (_toolRegistry.TryGetValue(toolName, out var tool))
        {
            return await tool(arguments);
        }
        throw new NotSupportedException($"Tool '{toolName}' is not registered");
    }

    private StructuredToolCall ParseSingleToolCall(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        
        var id = root.GetProperty("id").GetString() ?? "";
        var function = root.GetProperty("function");
        var name = function.GetProperty("name").GetString() ?? "";
        var argsJson = function.GetProperty("arguments").GetString() ?? "{}";
        
        return StructuredArgumentParser.CreateToolCall(id, name, argsJson);
    }

    private async Task ValidateAndExecuteTool(StructuredToolCall toolCall, ExampleToolDeclaration declaration)
    {
        Console.WriteLine($"Tool: {toolCall.Name}");
        
        if (toolCall.ParseError != null)
        {
            Console.WriteLine($"❌ Parse Error: {toolCall.ParseError.Message}");
            return;
        }
        
        // In a real implementation, validate against the schema
        var validationErrors = ValidateArguments(toolCall.Arguments, declaration.Parameters);
        
        if (validationErrors.Any())
        {
            Console.WriteLine("❌ Validation Errors:");
            foreach (var error in validationErrors)
            {
                Console.WriteLine($"  - {error}");
            }
        }
        else
        {
            Console.WriteLine("✅ Validation passed");
            
            if (toolCall.Arguments != null)
            {
                var result = await ExecuteTool(toolCall.Name, toolCall.Arguments);
                Console.WriteLine($"✅ Execution successful: {JsonSerializer.Serialize(result)}");
            }
        }
    }

    private List<string> ValidateArguments(Dictionary<string, object?>? args, ExampleToolParameters? parameters)
    {
        var errors = new List<string>();
        
        if (args == null || parameters == null)
            return errors;
        
        // Check required fields
        foreach (var required in parameters.Required ?? Array.Empty<string>())
        {
            if (!args.ContainsKey(required) || args[required] == null)
            {
                errors.Add($"Missing required field: {required}");
            }
        }
        
        // Additional validation would go here
        
        return errors;
    }
}

// Tool-related models - extend from base models to avoid conflicts
public class ExampleToolDeclaration
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ExampleToolParameters? Parameters { get; set; }
}

public class ExampleToolParameters
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ExampleParameterSchema>? Properties { get; set; }
    public string[]? Required { get; set; }
}

public class ExampleParameterSchema
{
    public string Type { get; set; } = "";
    public string? Description { get; set; }
    public string? Format { get; set; }
    public string[]? Enum { get; set; }
    public object? Default { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public ExampleParameterSchema? Items { get; set; }
    public Dictionary<string, ExampleParameterSchema>? Properties { get; set; }
}

// Tool execution pipeline
public class ToolExecutionPipeline
{
    private readonly Dictionary<string, Func<Dictionary<string, object?>, Task<object>>> _tools;

    public ToolExecutionPipeline(Dictionary<string, Func<Dictionary<string, object?>, Task<object>>> tools)
    {
        _tools = tools;
    }

    public async Task<List<StructuredToolResult>> ExecuteToolCalls(List<StructuredToolCall> toolCalls)
    {
        var results = new List<StructuredToolResult>();
        
        foreach (var toolCall in toolCalls)
        {
            if (toolCall.Arguments != null && _tools.TryGetValue(toolCall.Name, out var tool))
            {
                try
                {
                    var result = await tool(toolCall.Arguments);
                    results.Add(new StructuredToolResult
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        Result = result,
                        IsSuccess = true
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new StructuredToolResult
                    {
                        CallId = toolCall.Id,
                        ToolName = toolCall.Name,
                        IsSuccess = false,
                        ErrorMessage = ex.Message,
                        ExecutionError = ex
                    });
                }
            }
        }
        
        return results;
    }
}