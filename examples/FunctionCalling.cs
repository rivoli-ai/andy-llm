using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Configuration;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

/// <summary>
/// Example demonstrating function/tool calling with LLMs
/// </summary>
public class FunctionCalling
{
    public static async Task Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.ConfigureLlmFromEnvironment();
        services.AddLlmServices(options =>
        {
            options.DefaultProvider = "openai";
            options.DefaultModel = "gpt-4o-mini";
        });

        var serviceProvider = services.BuildServiceProvider();
        var llmClient = serviceProvider.GetRequiredService<LlmClient>();

        // Create a conversation context with tools
        var context = new ConversationContext
        {
            SystemInstruction = "You are a helpful assistant with access to various tools. Use them when appropriate to help the user."
        };

        // Define available tools
        context.AvailableTools.Add(new ToolDeclaration
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
                        ["enum"] = new[] { "fahrenheit", "celsius" },
                        ["description"] = "Temperature unit"
                    }
                },
                ["required"] = new[] { "location" }
            }
        });

        context.AvailableTools.Add(new ToolDeclaration
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
                        ["description"] = "Mathematical expression to evaluate"
                    }
                },
                ["required"] = new[] { "expression" }
            }
        });

        Console.WriteLine("Function Calling Example");
        Console.WriteLine("========================");
        Console.WriteLine("Ask me about weather or to calculate something!");
        Console.WriteLine("Type 'exit' to quit\n");

        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            // Add user message to context
            context.AddUserMessage(input);

            // Create request with tools
            var request = context.CreateRequest();

            try
            {
                var response = await llmClient.CompleteAsync(request);

                // Check if the assistant wants to call functions
                if (response.FunctionCalls?.Any() == true)
                {
                    Console.WriteLine($"Assistant wants to call {response.FunctionCalls.Count} function(s):");
                    
                    // Add assistant message with function calls
                    context.AddAssistantMessageWithToolCalls(response.Content, response.FunctionCalls);

                    // Process each function call
                    foreach (var functionCall in response.FunctionCalls)
                    {
                        Console.WriteLine($"  - Calling {functionCall.Name}...");
                        
                        var result = ExecuteFunction(functionCall);
                        
                        // Add tool response to context
                        context.AddToolResponse(functionCall.Name, functionCall.Id, result);
                    }

                    // Get final response after function execution
                    var finalRequest = context.CreateRequest();
                    var finalResponse = await llmClient.CompleteAsync(finalRequest);
                    
                    Console.WriteLine($"Assistant: {finalResponse.Content}\n");
                    context.AddAssistantMessage(finalResponse.Content);
                }
                else
                {
                    // Regular response without function calls
                    Console.WriteLine($"Assistant: {response.Content}\n");
                    context.AddAssistantMessage(response.Content);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}\n");
            }
        }

        Console.WriteLine("Goodbye!");
    }

    private static object ExecuteFunction(FunctionCall functionCall)
    {
        switch (functionCall.Name)
        {
            case "get_weather":
                var location = functionCall.Arguments.GetValueOrDefault("location")?.ToString() ?? "";
                var unit = functionCall.Arguments.GetValueOrDefault("unit")?.ToString() ?? "fahrenheit";
                
                // Simulate weather API call
                var random = new Random();
                var temp = unit == "celsius" ? random.Next(10, 35) : random.Next(50, 95);
                var conditions = new[] { "sunny", "cloudy", "rainy", "partly cloudy" };
                var condition = conditions[random.Next(conditions.Length)];
                
                return new
                {
                    location = location,
                    temperature = temp,
                    unit = unit,
                    condition = condition,
                    humidity = random.Next(30, 80),
                    wind_speed = random.Next(5, 25)
                };
                
            case "calculate":
                var expression = functionCall.Arguments.GetValueOrDefault("expression")?.ToString() ?? "";
                
                try
                {
                    // Simple evaluation (in production, use a proper expression evaluator)
                    // This is just for demonstration
                    var result = EvaluateSimpleExpression(expression);
                    return new { result = result, expression = expression };
                }
                catch
                {
                    return new { error = "Invalid expression", expression = expression };
                }
                
            default:
                return new { error = $"Unknown function: {functionCall.Name}" };
        }
    }

    private static double EvaluateSimpleExpression(string expression)
    {
        // This is a very simple evaluator for demonstration
        // In production, use a proper math expression library
        expression = expression.Replace(" ", "");
        
        // Handle simple operations
        if (expression.Contains('+'))
        {
            var parts = expression.Split('+');
            return double.Parse(parts[0]) + double.Parse(parts[1]);
        }
        if (expression.Contains('-'))
        {
            var parts = expression.Split('-');
            return double.Parse(parts[0]) - double.Parse(parts[1]);
        }
        if (expression.Contains('*'))
        {
            var parts = expression.Split('*');
            return double.Parse(parts[0]) * double.Parse(parts[1]);
        }
        if (expression.Contains('/'))
        {
            var parts = expression.Split('/');
            return double.Parse(parts[0]) / double.Parse(parts[1]);
        }
        
        return double.Parse(expression);
    }
}