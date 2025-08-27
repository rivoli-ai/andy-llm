# Getting Started with Andy.Llm

This guide will help you understand and use the Andy.Llm library effectively.

## Prerequisites

- .NET 8.0 or later
- An API key from at least one supported provider (OpenAI, Cerebras, Azure OpenAI, etc.)
- Basic understanding of async/await in C#

## Installation

```bash
dotnet add package Andy.Llm
```

## Core Concepts

### 1. Providers

A **Provider** is an implementation that connects to a specific LLM service (OpenAI, Cerebras, etc.). Each provider:
- Handles API authentication
- Translates requests to provider-specific format
- Manages streaming and error handling
- Implements the `ILlmProvider` interface

### 2. Messages

Messages represent the conversation between user and AI. The library uses a flexible message system:

```csharp
// Simple text message
var message = Message.CreateText(MessageRole.User, "Hello!");

// Message with multiple parts
var complexMessage = new Message
{
    Role = MessageRole.Assistant,
    Parts = new List<MessagePart>
    {
        new TextPart { Text = "I'll check the weather." },
        new ToolCallPart 
        { 
            ToolName = "get_weather",
            CallId = "call_123",
            Arguments = new() { ["location"] = "NYC" }
        }
    }
};
```

### 3. Conversation Context

`ConversationContext` manages the conversation history and automatically handles:
- Token limits
- System instructions
- Tool declarations
- Message ordering

## Basic Usage Patterns

### Pattern 1: Simple Q&A

```csharp
using Andy.Llm;

var client = new LlmClient("your-api-key");
var response = await client.GetResponseAsync("What is the capital of France?");
Console.WriteLine(response); // "The capital of France is Paris."
```

### Pattern 2: Managed Conversation

```csharp
using Andy.Llm;
using Andy.Llm.Models;

var client = new LlmClient("your-api-key");
var context = new ConversationContext
{
    SystemInstruction = "You are a helpful geography tutor."
};

// First question
context.AddUserMessage("What is the capital of France?");
var request1 = context.CreateRequest();
var response1 = await client.CompleteAsync(request1);
context.AddAssistantMessage(response1.Content);

// Follow-up question (maintains context)
context.AddUserMessage("What about Germany?");
var request2 = context.CreateRequest();
var response2 = await client.CompleteAsync(request2);
context.AddAssistantMessage(response2.Content);
```

### Pattern 3: Streaming for Real-Time Output

```csharp
var request = new LlmRequest
{
    Messages = new List<Message>
    {
        Message.CreateText(MessageRole.User, "Write a haiku about programming")
    },
    Stream = true
};

await foreach (var chunk in client.StreamCompleteAsync(request))
{
    if (!string.IsNullOrEmpty(chunk.TextDelta))
    {
        Console.Write(chunk.TextDelta); // Prints as it arrives
    }
}
```

### Pattern 4: Function Calling

```csharp
var context = new ConversationContext();

// 1. Declare available tools
context.AvailableTools.Add(new ToolDeclaration
{
    Name = "calculate",
    Description = "Performs mathematical calculations",
    Parameters = new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["expression"] = new 
            { 
                type = "string", 
                description = "Math expression to evaluate" 
            }
        },
        ["required"] = new[] { "expression" }
    }
});

// 2. Send user request
context.AddUserMessage("What's 15% of 240?");
var request = context.CreateRequest();
var response = await client.CompleteAsync(request);

// 3. Check if AI wants to call a function
if (response.FunctionCalls.Any())
{
    foreach (var call in response.FunctionCalls)
    {
        if (call.Name == "calculate")
        {
            // 4. Execute the function
            var expression = call.Arguments["expression"]?.ToString();
            var result = EvaluateExpression(expression); // Your implementation
            
            // 5. Add result to context
            context.AddToolResponse("calculate", call.Id, result);
        }
    }
    
    // 6. Get final response with function results
    var finalRequest = context.CreateRequest();
    var finalResponse = await client.CompleteAsync(finalRequest);
    Console.WriteLine(finalResponse.Content);
}
```

## Configuration Options

### Using Environment Variables

Set these before running your application:

```bash
# For OpenAI
export OPENAI_API_KEY="sk-..."
export OPENAI_MODEL="gpt-4o-mini"

# For Cerebras
export CEREBRAS_API_KEY="..."
export CEREBRAS_MODEL="llama3.1-8b"
```

Then in code:
```csharp
services.ConfigureLlmFromEnvironment();
```

### Using appsettings.json

```json
{
  "Llm": {
    "DefaultProvider": "openai",
    "DefaultModel": "gpt-4o-mini",
    "Providers": {
      "openai": {
        "ApiKey": "sk-...",
        "Model": "gpt-4o-mini",
        "Organization": "org-..."
      },
      "cerebras": {
        "ApiKey": "...",
        "Model": "llama3.1-70b"
      }
    }
  }
}
```

### Programmatic Configuration

```csharp
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
    options.DefaultTemperature = 0.7;
    options.DefaultMaxTokens = 2000;
    
    options.Providers["openai"] = new ProviderConfig
    {
        ApiKey = configuration["OpenAI:ApiKey"],
        Model = "gpt-4o",
        Enabled = true
    };
});
```

## Provider Selection

### Explicit Provider Selection

```csharp
var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

// Get specific provider
var openAi = factory.CreateProvider("openai");
var response = await openAi.CompleteAsync(request);
```

### Automatic Fallback

```csharp
// Automatically selects first available provider
var provider = await factory.CreateAvailableProviderAsync();
var response = await provider.CompleteAsync(request);
```

### Provider Availability Check

```csharp
var provider = factory.CreateProvider("openai");
if (await provider.IsAvailableAsync())
{
    // Provider is ready to use
}
```

## Error Handling

### Common Errors and Solutions

```csharp
try
{
    var response = await client.CompleteAsync(request);
}
catch (InvalidOperationException ex)
{
    // Configuration error (missing API key, etc.)
    logger.LogError(ex, "Configuration error");
}
catch (ClientResultException ex)
{
    // API error (rate limit, invalid request, etc.)
    logger.LogError(ex, "API error: {StatusCode}", ex.Status);
}
catch (TaskCanceledException ex)
{
    // Request timeout
    logger.LogError(ex, "Request timed out");
}
```

### Retry Logic

```csharp
public async Task<LlmResponse> CompleteWithRetryAsync(LlmRequest request)
{
    const int maxRetries = 3;
    var delay = TimeSpan.FromSeconds(1);
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            return await client.CompleteAsync(request);
        }
        catch (ClientResultException ex) when (ex.Status == 429) // Rate limit
        {
            if (i == maxRetries - 1) throw;
            
            await Task.Delay(delay);
            delay *= 2; // Exponential backoff
        }
    }
    
    throw new InvalidOperationException("Max retries exceeded");
}
```

## Best Practices

### 1. Use Dependency Injection

```csharp
// ✅ Good: Use DI for better testability
public class ChatService
{
    private readonly LlmClient _client;
    
    public ChatService(LlmClient client)
    {
        _client = client;
    }
}

// ❌ Avoid: Direct instantiation
public class ChatService
{
    private readonly LlmClient _client = new LlmClient("key");
}
```

### 2. Manage Context Properly

```csharp
// ✅ Good: Set reasonable limits
var context = new ConversationContext
{
    MaxContextMessages = 20,      // Limit message count
    MaxContextCharacters = 50000  // Limit total size
};

// ❌ Avoid: Unlimited context growth
var context = new ConversationContext(); // No limits set
```

### 3. Handle Streaming Properly

```csharp
// ✅ Good: Handle cancellation
await foreach (var chunk in client.StreamCompleteAsync(request, cancellationToken))
{
    if (cancellationToken.IsCancellationRequested)
        break;
    
    ProcessChunk(chunk);
}

// ❌ Avoid: No cancellation support
await foreach (var chunk in client.StreamCompleteAsync(request))
{
    ProcessChunk(chunk); // Can't cancel
}
```

### 4. Use Appropriate Models

```csharp
// ✅ Good: Choose model based on task
var request = new LlmRequest
{
    Model = simpleTask ? "gpt-3.5-turbo" : "gpt-4o",
    // ...
};

// ❌ Avoid: Always using most expensive model
var request = new LlmRequest
{
    Model = "gpt-4o", // Overkill for simple tasks
    // ...
};
```

### 5. Secure API Keys

```csharp
// ✅ Good: Use secure storage
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var apiKey = configuration["OpenAI:ApiKey"]; // From secure config

// ❌ Never: Hardcode keys
var apiKey = "sk-abc123..."; // Security risk!
```

## Testing Your Integration

### Unit Testing with Mocks

```csharp
[Fact]
public async Task ChatService_Should_Return_Response()
{
    // Arrange
    var mockProvider = new Mock<ILlmProvider>();
    mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LlmRequest>(), default))
        .ReturnsAsync(new LlmResponse { Content = "Test response" });
    
    var client = new LlmClient(mockProvider.Object, Mock.Of<ILogger<LlmClient>>());
    
    // Act
    var response = await client.CompleteAsync(new LlmRequest());
    
    // Assert
    Assert.Equal("Test response", response.Content);
}
```

### Integration Testing

```csharp
[SkippableFact]
public async Task RealProvider_Should_Work()
{
    // Skip if no API key
    Skip.IfNot(Environment.GetEnvironmentVariable("OPENAI_API_KEY") != null);
    
    // Real provider test
    var provider = new OpenAIProvider(options, logger);
    var response = await provider.CompleteAsync(request);
    
    Assert.NotEmpty(response.Content);
}
```

## Troubleshooting

### Issue: "API key not configured"
**Solution**: Ensure environment variable is set or configuration is loaded correctly.

### Issue: "Provider not available"
**Solution**: Check network connection and API endpoint accessibility.

### Issue: "Model not found"
**Solution**: Verify model name is correct for the provider.

### Issue: "Rate limit exceeded"
**Solution**: Implement retry logic with exponential backoff.

### Issue: "Context length exceeded"
**Solution**: Reduce `MaxContextMessages` or implement summarization.

## Next Steps

1. Explore the [examples](../examples/) directory
2. Read the [architecture documentation](ARCHITECTURE.md)
3. Check the [API reference](API_REFERENCE.md)
4. Join our community discussions