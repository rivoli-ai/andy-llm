using Andy.Llm;
using Andy.Llm.Models;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

// Example: Function calling with tool responses

// Determine which provider to use
var providerEnv = Environment.GetEnvironmentVariable("LLM_PROVIDER") ?? "openai";
var provider = providerEnv.ToLower();
var model = provider == "cerebras" ? "llama-3.3-70b" : "gpt-4o-mini";

var services = new ServiceCollection();
services.AddLogging(builder => 
{
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
        options.TimestampFormat = "";
    });
    builder.SetMinimumLevel(LogLevel.Information);
    // Hide HTTP client logs
    builder.AddFilter("System.Net.Http", LogLevel.Warning);
    builder.AddFilter("Andy.Llm.Providers", LogLevel.Warning);
});
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = provider;
});

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Using provider: {Provider}, model: {Model}", provider, model);
    
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

    // Create conversation context with tools
    var context = new ConversationContext
    {
        SystemInstruction = "You are a helpful assistant with access to weather information.",
        AvailableTools = { weatherTool }
    };

    logger.LogInformation("=== Function Calling Example ===");
    logger.LogInformation("Provider: {Provider}, Model: {Model}", provider.ToUpper(), model);
    logger.LogInformation("Ask about the weather in any city!\n");

    // Example interaction
    var userMessage = "What's the weather like in San Francisco?";
    logger.LogInformation("User: {Message}", userMessage);

    context.AddUserMessage(userMessage);

    // Create request with tools
    var request = context.CreateRequest(model);

    // Get initial response
    var response = await client.CompleteAsync(request);

    // Check if the model wants to call functions
    if (response.FunctionCalls != null && response.FunctionCalls.Any())
    {
        logger.LogInformation("Model wants to call functions:");
        foreach (var call in response.FunctionCalls)
        {
            logger.LogInformation("  Function: {Name}", call.Name);
            logger.LogInformation("  Arguments: {Args}", JsonSerializer.Serialize(call.Arguments));

            // Simulate function execution
            if (call.Name == "get_weather")
            {
                var location = call.Arguments["location"]?.ToString() ?? "Unknown";
                var unit = call.Arguments.ContainsKey("unit") 
                    ? call.Arguments["unit"]?.ToString() 
                    : "fahrenheit";

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

                logger.LogInformation("Executed function: {Result}", JsonSerializer.Serialize(weatherData));

                // Add tool response to context
                context.AddToolResponse(
                    call.Name,
                    call.Id,
                    JsonSerializer.Serialize(weatherData)
                );
            }
        }

        // Get final response with function results
        logger.LogInformation("\nGetting final response with function results...");
        var finalRequest = context.CreateRequest(model);
        var finalResponse = await client.CompleteAsync(finalRequest);
        logger.LogInformation("Assistant: {Response}", finalResponse.Content);
    }
    else
    {
        // No function calls, just display the response
        logger.LogInformation("Assistant: {Response}", response.Content);
    }

    // Interactive mode
    logger.LogInformation("\n=== Interactive Mode ===");
    logger.LogInformation("Type 'exit' to quit\n");

    while (true)
    {
        Console.Write("You: ");
        var input = Console.ReadLine();

        if (string.IsNullOrEmpty(input) || input.ToLower() == "exit")
            break;

        try
        {
            context.AddUserMessage(input);
            request = context.CreateRequest(model);
            response = await client.CompleteAsync(request);

            // Handle function calls
            if (response.FunctionCalls != null && response.FunctionCalls.Any())
            {
                foreach (var call in response.FunctionCalls)
                {
                    logger.LogInformation("Calling function: {Name}", call.Name);
                    
                    if (call.Name == "get_weather")
                    {
                        var location = call.Arguments["location"]?.ToString() ?? "Unknown";
                        var weatherData = new
                        {
                            location = location,
                            temperature = Random.Shared.Next(60, 85),
                            unit = "fahrenheit",
                            condition = new[] { "Sunny", "Cloudy", "Rainy", "Partly cloudy" }[Random.Shared.Next(4)],
                            humidity = Random.Shared.Next(40, 80)
                        };

                        context.AddToolResponse(
                            call.Name,
                            call.Id,
                            JsonSerializer.Serialize(weatherData)
                        );
                    }
                }

                // Get final response
                var finalRequest = context.CreateRequest(model);
                var finalResponse = await client.CompleteAsync(finalRequest);
                logger.LogInformation("Assistant: {Response}", finalResponse.Content);
                context.AddAssistantMessage(finalResponse.Content);
            }
            else
            {
                logger.LogInformation("Assistant: {Response}", response.Content);
                context.AddAssistantMessage(response.Content);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Error processing request: {Message}", ex.Message);
            logger.LogDebug(ex, "Full error details");
        }

        Console.WriteLine();
    }

    logger.LogInformation("Goodbye!");
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during the function calling example");
}

// Simple Program class for logger
partial class Program { }