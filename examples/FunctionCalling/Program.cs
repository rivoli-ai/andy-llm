using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Andy.Llm.Extensions;
using Andy.Llm.Examples.Shared;
using Andy.Llm.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// Example: Function calling with tool responses

// Load configuration from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddCleanConsole());
services.AddLlmServices(configuration);
services.ConfigureLlmFromEnvironment();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("\n=== Function Calling Example ===");

    var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();
    var llmProvider = await factory.CreateAvailableProviderAsync();

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

    var calculateTool = new ToolDeclaration
    {
        Name = "calculate",
        Description = "Perform basic arithmetic calculations",
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

    // Create conversation context with messages
    var messages = new List<Message>
    {
        new Message { Role = Role.System, Content = "You are a helpful assistant with access to weather data and calculation tools." }
    };

    // Initial user query
    var userInput = "What's the weather in San Francisco and how much is 15% of 240?";
    logger.LogInformation("User: {Input}", userInput);

    messages.Add(new Message { Role = Role.User, Content = userInput });

    // Create request with tools
    var request = new LlmRequest
    {
        Messages = messages,
        Tools = new List<ToolDeclaration> { weatherTool, calculateTool },
        Config = new LlmClientConfig { MaxTokens = 1000 }
    };

    // Get initial response
    var response = await llmProvider.CompleteAsync(request);

    // Check if the model wants to call functions
    if (response.ToolCalls != null && response.ToolCalls.Any())
    {
        logger.LogInformation("\nModel wants to call functions:");

        // Add the assistant's message with tool calls to messages
        var assistantMessage = new Message
        {
            Role = Role.Assistant,
            Content = response.Content ?? string.Empty,
            ToolCalls = response.ToolCalls
        };
        messages.Add(assistantMessage);

        foreach (var call in response.ToolCalls)
        {
            logger.LogInformation("  Function: {Name}", call.Name);
            logger.LogInformation("  Arguments: {Args}", call.ArgumentsJson);

            string resultContent = string.Empty;

            // Simulate function execution
            if (call.Name == "get_weather")
            {
                var weatherArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.ArgumentsJson);
                var location = weatherArgs?["location"].GetString() ?? "Unknown";
                var unit = weatherArgs?.ContainsKey("unit") == true ? weatherArgs["unit"].GetString() : "fahrenheit";

                // Simulated weather data
                var weatherData = new
                {
                    location = location,
                    temperature = 72,
                    unit = unit,
                    condition = "Partly cloudy",
                    humidity = 65,
                    wind_speed = 12
                };

                resultContent = JsonSerializer.Serialize(weatherData);
                logger.LogInformation("  Result: {Result}", resultContent);
            }
            else if (call.Name == "calculate")
            {
                var calcArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.ArgumentsJson);
                var expression = calcArgs?["expression"].GetString() ?? "";

                // Simple calculation (in production, use a proper expression evaluator)
                if (expression.Contains("15% of 240") || expression.Contains("0.15 * 240"))
                {
                    resultContent = JsonSerializer.Serialize(new { result = 36, expression = expression });
                }
                else
                {
                    resultContent = JsonSerializer.Serialize(new { error = "Cannot evaluate expression" });
                }

                logger.LogInformation("  Result: {Result}", resultContent);
            }

            // Add tool result message with proper ToolResult
            var toolMessage = new Message
            {
                Role = Role.Tool,
                Content = resultContent,
                ToolResults = new List<ToolResult>
                {
                    new ToolResult
                    {
                        CallId = call.Id,
                        Name = call.Name,
                        ResultJson = resultContent,
                        IsError = false
                    }
                }
            };
            messages.Add(toolMessage);
        }

        // Get final response with function results
        logger.LogInformation("\nGetting final response with function results...");
        var finalRequest = new LlmRequest
        {
            Messages = messages,
            Tools = request.Tools,
            Config = request.Config
        };
        var finalResponse = await llmProvider.CompleteAsync(finalRequest);
        logger.LogInformation("Assistant: {Response}", finalResponse.Content);

        messages.Add(new Message { Role = Role.Assistant, Content = finalResponse.Content });
    }
    else
    {
        // No function calls, just display the response
        logger.LogInformation("Assistant: {Response}", response.Content);
        messages.Add(new Message { Role = Role.Assistant, Content = response.Content });
    }

    // Interactive mode
    logger.LogInformation("\n=== Interactive Mode ===");
    logger.LogInformation("Type 'exit' to quit");

    while (true)
    {
        Console.Write("You: ");
        var input = Console.ReadLine();

        if (string.IsNullOrEmpty(input) || input.ToLower() == "exit")
        {
            break;
        }

        try
        {
            messages.Add(new Message { Role = Role.User, Content = input });

            // Keep conversation manageable
            if (messages.Count > 20)
            {
                // Keep system message and last 15 messages
                var systemMessage = messages.First(m => m.Role == Role.System);
                messages = new List<Message> { systemMessage };
                messages.AddRange(messages.Skip(messages.Count - 15));
            }

            var newRequest = new LlmRequest
            {
                Messages = messages,
                Tools = request.Tools,
                Config = request.Config
            };
            response = await llmProvider.CompleteAsync(newRequest);

            // Handle function calls
            if (response.ToolCalls != null && response.ToolCalls.Any())
            {
                var assistantMsg = new Message
                {
                    Role = Role.Assistant,
                    Content = response.Content ?? string.Empty,
                    ToolCalls = response.ToolCalls
                };
                messages.Add(assistantMsg);

                foreach (var call in response.ToolCalls)
                {
                    logger.LogInformation("Calling function: {Name}", call.Name);

                    // Execute function and add result (simplified for demo)
                    var result = $"{{\"result\": \"Function {call.Name} executed successfully\"}}";
                    messages.Add(new Message
                    {
                        Role = Role.Tool,
                        Content = result
                    });
                }

                // Get final response with results
                var finalRequest = new LlmRequest
                {
                    Messages = messages,
                    Tools = request.Tools,
                    Config = request.Config
                };
                var finalResp = await llmProvider.CompleteAsync(finalRequest);
                logger.LogInformation("Assistant: {Response}", finalResp.Content);
                messages.Add(new Message { Role = Role.Assistant, Content = finalResp.Content });
            }
            else
            {
                logger.LogInformation("Assistant: {Response}", response.Content);
                messages.Add(new Message { Role = Role.Assistant, Content = response.Content });
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error: {Message}", ex.Message);
        }
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during function calling example");
}

// Simple Program class for logger
partial class Program { }