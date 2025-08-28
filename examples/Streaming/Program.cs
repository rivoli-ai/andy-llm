using Andy.Llm;
using Andy.Llm.Extensions;
using Andy.Llm.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Example demonstrating streaming responses with Andy.Llm
/// </summary>
public class StreamingExample
{
    public static async Task Main(string[] args)
    {
        // Setup
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.ConfigureLlmFromEnvironment();
        
        var serviceProvider = services.BuildServiceProvider();
        var llmClient = serviceProvider.GetRequiredService<LlmClient>();

        // Example 1: Basic streaming
        await BasicStreaming(llmClient);

        // Example 2: Streaming with cancellation
        await StreamingWithCancellation(llmClient);

        // Example 3: Streaming with progress display
        await StreamingWithProgress(llmClient);

        // Example 4: Streaming with error handling
        await StreamingWithErrorHandling(llmClient);

        // Example 5: Streaming function calls
        await StreamingWithFunctionCalls(llmClient);

        Console.WriteLine("\nStreaming examples completed!");
    }

    static async Task BasicStreaming(LlmClient client)
    {
        Console.WriteLine("\n=== Example 1: Basic Streaming ===");
        Console.WriteLine("Prompt: Tell me a short story about streaming data\n");

        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.System, "You are a helpful assistant."),
                Message.CreateText(MessageRole.User, "Tell me a short story about streaming data")
            },
            Stream = true,
            MaxTokens = 200
        };

        Console.Write("Response: ");
        var fullResponse = "";
        
        await foreach (var chunk in client.StreamCompleteAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                Console.Write(chunk.TextDelta);
                fullResponse += chunk.TextDelta;
            }

            if (chunk.IsComplete)
            {
                Console.WriteLine("\n\n[Stream completed]");
            }
        }

        Console.WriteLine($"Total characters received: {fullResponse.Length}");
    }

    static async Task StreamingWithCancellation(LlmClient client)
    {
        Console.WriteLine("\n=== Example 2: Streaming with Cancellation ===");
        Console.WriteLine("This example will cancel after receiving 50 characters\n");

        var cts = new CancellationTokenSource();
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Count from 1 to 100 slowly")
            },
            Stream = true
        };

        var charCount = 0;
        Console.Write("Response: ");

        try
        {
            await foreach (var chunk in client.StreamCompleteAsync(request, cts.Token))
            {
                if (!string.IsNullOrEmpty(chunk.TextDelta))
                {
                    Console.Write(chunk.TextDelta);
                    charCount += chunk.TextDelta.Length;

                    // Cancel after 50 characters
                    if (charCount >= 50)
                    {
                        Console.WriteLine("\n\n[Cancelling stream...]");
                        cts.Cancel();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Stream cancelled successfully]");
        }

        Console.WriteLine($"Received {charCount} characters before cancellation");
    }

    static async Task StreamingWithProgress(LlmClient client)
    {
        Console.WriteLine("\n=== Example 3: Streaming with Progress Display ===");
        
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "List 5 interesting facts about space")
            },
            Stream = true,
            MaxTokens = 300
        };

        var tokenCount = 0;
        var startTime = DateTime.UtcNow;
        var response = "";

        Console.WriteLine("Streaming response with live stats:\n");
        var cursorTop = Console.CursorTop;

        await foreach (var chunk in client.StreamCompleteAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                response += chunk.TextDelta;
                tokenCount++;

                // Update display
                Console.SetCursorPosition(0, cursorTop);
                Console.WriteLine(response);
                Console.WriteLine(new string('-', 50));
                
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                var tokensPerSecond = tokenCount / elapsed;
                
                Console.WriteLine($"Tokens: {tokenCount} | Speed: {tokensPerSecond:F1} tokens/sec | Time: {elapsed:F1}s");
            }

            if (chunk.IsComplete)
            {
                Console.WriteLine("\n[Stream completed]");
            }
        }
    }

    static async Task StreamingWithErrorHandling(LlmClient client)
    {
        Console.WriteLine("\n=== Example 4: Streaming with Error Handling ===");
        
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                Message.CreateText(MessageRole.User, "Generate text")
            },
            Stream = true,
            MaxTokens = 100,
            Temperature = 0.8
        };

        var retryCount = 0;
        const int maxRetries = 3;
        
        while (retryCount < maxRetries)
        {
            try
            {
                Console.WriteLine($"Attempt {retryCount + 1} of {maxRetries}...");
                Console.Write("Response: ");

                await foreach (var chunk in client.StreamCompleteAsync(request))
                {
                    if (!string.IsNullOrEmpty(chunk.Error))
                    {
                        throw new Exception($"Stream error: {chunk.Error}");
                    }

                    if (!string.IsNullOrEmpty(chunk.TextDelta))
                    {
                        Console.Write(chunk.TextDelta);
                    }

                    if (chunk.IsComplete)
                    {
                        Console.WriteLine("\n[Success]");
                        return; // Success, exit retry loop
                    }
                }
                
                return; // Success
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[Error: {ex.Message}]");
                retryCount++;
                
                if (retryCount < maxRetries)
                {
                    var delay = Math.Pow(2, retryCount) * 1000;
                    Console.WriteLine($"Retrying in {delay}ms...\n");
                    await Task.Delay((int)delay);
                }
                else
                {
                    Console.WriteLine("Max retries exceeded. Stream failed.");
                }
            }
        }
    }

    static async Task StreamingWithFunctionCalls(LlmClient client)
    {
        Console.WriteLine("\n=== Example 5: Streaming with Function Calls ===");
        
        var context = new ConversationContext();
        
        // Define a tool
        context.AvailableTools.Add(new ToolDeclaration
        {
            Name = "get_current_time",
            Description = "Get the current time in a specified timezone",
            Parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["timezone"] = new 
                    { 
                        type = "string", 
                        description = "The timezone (e.g., 'UTC', 'EST', 'PST')" 
                    }
                },
                ["required"] = new[] { "timezone" }
            }
        });

        context.AddUserMessage("What time is it in Tokyo?");
        
        var request = context.CreateRequest();
        request.Stream = true;

        Console.WriteLine("Streaming response with potential function calls:\n");
        
        var functionCallDetected = false;
        var responseText = "";

        await foreach (var chunk in client.StreamCompleteAsync(request))
        {
            if (!string.IsNullOrEmpty(chunk.TextDelta))
            {
                Console.Write(chunk.TextDelta);
                responseText += chunk.TextDelta;
            }

            if (chunk.FunctionCall != null && !functionCallDetected)
            {
                functionCallDetected = true;
                Console.WriteLine($"\n\n[Function Call Detected]");
                Console.WriteLine($"Function: {chunk.FunctionCall.Name}");
                Console.WriteLine($"Arguments: {string.Join(", ", chunk.FunctionCall.Arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
                
                // Simulate function execution
                var result = GetCurrentTime("Asia/Tokyo");
                Console.WriteLine($"Result: {result}\n");
                
                // Add function result to context
                context.AddToolResponse(
                    chunk.FunctionCall.Name,
                    chunk.FunctionCall.Id,
                    result);
            }

            if (chunk.IsComplete)
            {
                Console.WriteLine("\n[Stream completed]");
                
                if (functionCallDetected)
                {
                    // Continue conversation with function result
                    Console.WriteLine("\nContinuing with function result...");
                    var followUpRequest = context.CreateRequest();
                    followUpRequest.Stream = true;
                    
                    await foreach (var followUpChunk in client.StreamCompleteAsync(followUpRequest))
                    {
                        if (!string.IsNullOrEmpty(followUpChunk.TextDelta))
                        {
                            Console.Write(followUpChunk.TextDelta);
                        }
                    }
                }
            }
        }
    }

    static string GetCurrentTime(string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                timezone.Replace("Asia/Tokyo", "Tokyo Standard Time"));
            var time = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return $"Current time in {timezone}: {time:yyyy-MM-dd HH:mm:ss}";
        }
        catch
        {
            return $"Unable to get time for timezone: {timezone}";
        }
    }
}