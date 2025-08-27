using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// Example: Function calling with tool responses

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
});

var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<LlmClient>();

// Define available tools
var weatherTool = new ToolDeclaration
{
    Name = "get_weather",
    Description = "Get the current weather for a location",
    Parameters = new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["location"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "The city and state, e.g., San Francisco, CA"
            },
            ["unit"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = new[] { "celsius", "fahrenheit" },
                ["description"] = "The temperature unit"
            }
        },
        ["required"] = new[] { "location" }
    }
};

var calculatorTool = new ToolDeclaration
{
    Name = "calculate",
    Description = "Perform mathematical calculations",
    Parameters = new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["expression"] = new Dictionary<string, object>
            {
                ["type"] = "string",
                ["description"] = "The mathematical expression to evaluate"
            }
        },
        ["required"] = new[] { "expression" }
    }
};

// Create conversation with tools
var conversation = new ConversationContext
{
    SystemInstruction = "You are a helpful assistant that can check weather and perform calculations.",
    AvailableTools = new List<ToolDeclaration> { weatherTool, calculatorTool }
};

Console.WriteLine("=== Function Calling Example ===");
Console.WriteLine("Available tools: get_weather, calculate");
Console.WriteLine("Try asking: 'What's the weather in New York?' or 'Calculate 15% of 250'\n");

// Simulate tool implementations
object? ExecuteTool(string toolName, Dictionary<string, object?> arguments)
{
    switch (toolName)
    {
        case "get_weather":
            var location = arguments["location"]?.ToString() ?? "Unknown";
            var unit = arguments.ContainsKey("unit") ? arguments["unit"]?.ToString() : "fahrenheit";
            
            // Simulated weather data
            var temp = Random.Shared.Next(60, 85);
            return new
            {
                location = location,
                temperature = temp,
                unit = unit,
                condition = "partly cloudy",
                humidity = Random.Shared.Next(40, 70)
            };
            
        case "calculate":
            var expression = arguments["expression"]?.ToString() ?? "0";
            try
            {
                // Handle percentage calculations
                if (expression.Contains("%") || expression.Contains("*"))
                {
                    // Handle "4% of 5935" format
                    if (expression.Contains("% of"))
                    {
                        var parts = expression.Split(new[] { "% of" }, StringSplitOptions.TrimEntries);
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0], out var percentage) &&
                            double.TryParse(parts[1], out var value))
                        {
                            var result = (percentage / 100) * value;
                            return new { expression = expression, result = result };
                        }
                    }
                    // Handle "4% 5935" format
                    else if (expression.Contains("%"))
                    {
                        var parts = expression.Split('%');
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0].Trim(), out var percentage) &&
                            double.TryParse(parts[1].Trim(), out var value))
                        {
                            var result = (percentage / 100) * value;
                            return new { expression = expression, result = result };
                        }
                    }
                    // Handle "0.04 * 5935" format
                    else if (expression.Contains("*"))
                    {
                        var parts = expression.Split('*');
                        if (parts.Length == 2 && 
                            double.TryParse(parts[0].Trim(), out var multiplier) &&
                            double.TryParse(parts[1].Trim(), out var value))
                        {
                            var result = multiplier * value;
                            return new { expression = expression, result = result };
                        }
                    }
                }
                // Handle simple arithmetic
                else if (expression.Contains("+"))
                {
                    var parts = expression.Split('+');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0].Trim(), out var a) &&
                        double.TryParse(parts[1].Trim(), out var b))
                    {
                        return new { expression = expression, result = a + b };
                    }
                }
                else if (expression.Contains("-"))
                {
                    var parts = expression.Split('-');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0].Trim(), out var a) &&
                        double.TryParse(parts[1].Trim(), out var b))
                    {
                        return new { expression = expression, result = a - b };
                    }
                }
                else if (expression.Contains("/"))
                {
                    var parts = expression.Split('/');
                    if (parts.Length == 2 && 
                        double.TryParse(parts[0].Trim(), out var a) &&
                        double.TryParse(parts[1].Trim(), out var b) && b != 0)
                    {
                        return new { expression = expression, result = a / b };
                    }
                }
                // Handle single number
                else if (double.TryParse(expression.Trim(), out var singleValue))
                {
                    return new { expression = expression, result = singleValue };
                }
                
                return new { expression = expression, result = "Unable to calculate" };
            }
            catch
            {
                return new { expression = expression, error = "Invalid expression" };
            }
            
        default:
            return new { error = $"Unknown tool: {toolName}" };
    }
}

// Interactive loop
while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();
    
    if (string.IsNullOrEmpty(input) || input.ToLower() == "exit")
        break;
        
    // Add user message
    conversation.AddUserMessage(input);
    
    // Get response with function calling
    var request = conversation.CreateRequest("gpt-4o-mini");
    var response = await client.CompleteAsync(request);
    
    // Check for function calls
    if (response.FunctionCalls?.Any() == true)
    {
        Console.WriteLine($"Assistant: {response.Content}");
        
        // FIRST: Add assistant message with tool calls to conversation
        conversation.AddAssistantMessageWithToolCalls(response.Content, response.FunctionCalls);
        
        // THEN: Execute function calls and add tool responses
        foreach (var call in response.FunctionCalls)
        {
            Console.WriteLine($"[Calling {call.Name}...]");
            
            var result = ExecuteTool(call.Name, call.Arguments);
            var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine($"[Tool Result: {resultJson}]");
            
            // Add tool response to conversation (AFTER the assistant message with tool calls)
            conversation.AddToolResponse(call.Name, call.Id, result);
        }
        
        // Get final response after tool execution
        request = conversation.CreateRequest("gpt-4o-mini");
        var finalResponse = await client.CompleteAsync(request);
        Console.WriteLine($"Assistant: {finalResponse.Content}");
        conversation.AddAssistantMessage(finalResponse.Content);
    }
    else
    {
        // Regular response without function calls
        Console.WriteLine($"Assistant: {response.Content}");
        conversation.AddAssistantMessage(response.Content);
    }
    
    Console.WriteLine();
}

Console.WriteLine("Goodbye!");