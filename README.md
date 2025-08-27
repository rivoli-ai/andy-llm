# Andy.Llm

A flexible, provider-agnostic .NET library for integrating with Large Language Models (LLMs) through OpenAI-compatible APIs.

## 🎯 Design Goals

1. **Provider Agnostic**: Support multiple LLM providers through a unified interface
2. **OpenAI Compatibility**: First-class support for OpenAI-compatible APIs
3. **Extensibility**: Easy to add new providers without modifying core logic
4. **Type Safety**: Strongly-typed models and interfaces
5. **Modern .NET**: Built on .NET 8.0 with latest C# features
6. **Production Ready**: Comprehensive error handling, logging, and testing

## 🚀 Quick Start

### Installation

```bash
dotnet add package Andy.Llm
```

### Basic Usage

```csharp
using Andy.Llm;
using Andy.Llm.Models;

// Simple API key initialization
var client = new LlmClient("your-api-key");

// Send a message and get response
var response = await client.GetResponseAsync("Hello, how are you?");
Console.WriteLine(response);
```

### Advanced Usage with Dependency Injection

```csharp
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure from environment variables
services.ConfigureLlmFromEnvironment();

// Or configure programmatically
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
    options.DefaultModel = "gpt-4o-mini";
    options.Providers["openai"] = new ProviderConfig
    {
        ApiKey = "your-api-key",
        Model = "gpt-4o-mini"
    };
});

var serviceProvider = services.BuildServiceProvider();
var llmClient = serviceProvider.GetRequiredService<LlmClient>();
```

## 🔧 Configuration

### Environment Variables

The library supports configuration through environment variables for all major providers:

#### OpenAI
- `OPENAI_API_KEY` - Your OpenAI API key
- `OPENAI_MODEL` - Model to use (default: gpt-4o)
- `OPENAI_API_BASE` - Custom API endpoint (optional)
- `OPENAI_ORGANIZATION` - Organization ID (optional)

#### Cerebras
- `CEREBRAS_API_KEY` - Your Cerebras API key
- `CEREBRAS_MODEL` - Model to use (default: llama3.1-8b)

#### Azure OpenAI
- `AZURE_OPENAI_ENDPOINT` - Your Azure OpenAI endpoint
- `AZURE_OPENAI_KEY` - Your Azure OpenAI key
- `AZURE_OPENAI_DEPLOYMENT` - Your deployment name
- `AZURE_OPENAI_API_VERSION` - API version (default: 2024-02-15-preview)

#### Local/Ollama
- `OLLAMA_API_BASE` - Your local endpoint (e.g., http://localhost:11434)
- `OLLAMA_MODEL` - Model to use (e.g., llama2)

## 📚 Features

### Streaming Responses

```csharp
var request = new LlmRequest
{
    Messages = new List<Message>
    {
        Message.CreateText(MessageRole.User, "Write a story")
    },
    Stream = true
};

await foreach (var chunk in client.StreamCompleteAsync(request))
{
    if (!string.IsNullOrEmpty(chunk.TextDelta))
    {
        Console.Write(chunk.TextDelta);
    }
}
```

### Function/Tool Calling

```csharp
var context = new ConversationContext();

// Define available tools
context.AvailableTools.Add(new ToolDeclaration
{
    Name = "get_weather",
    Description = "Get current weather for a location",
    Parameters = new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["location"] = new { type = "string", description = "City and state" }
        },
        ["required"] = new[] { "location" }
    }
});

// Use in conversation
context.AddUserMessage("What's the weather in New York?");
var request = context.CreateRequest();
var response = await client.CompleteAsync(request);

// Handle function calls
if (response.FunctionCalls.Any())
{
    foreach (var call in response.FunctionCalls)
    {
        // Execute function and add result to context
        var result = ExecuteFunction(call);
        context.AddToolResponse(call.Name, call.Id, result);
    }
}
```

### Conversation Management

```csharp
var context = new ConversationContext
{
    SystemInstruction = "You are a helpful assistant.",
    MaxContextMessages = 50,
    MaxContextCharacters = 100000
};

// Build conversation
context.AddUserMessage("Hello!");
context.AddAssistantMessage("Hi! How can I help you?");
context.AddUserMessage("Tell me about AI");

// Context automatically manages token limits
var request = context.CreateRequest();
```

### Multi-Provider Support

```csharp
var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

// Use specific provider
var openAiProvider = factory.CreateProvider("openai");
var cerebrasProvider = factory.CreateProvider("cerebras");

// Or get first available provider
var provider = await factory.CreateAvailableProviderAsync();
```

## 🏗️ Architecture

See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed architecture documentation.

## 🧪 Testing

The library includes comprehensive unit and integration tests:

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run only unit tests
dotnet test --filter "Category!=Integration"
```

## 📖 Examples

Check the [examples](examples/) directory for complete examples:
- [SimpleChat.cs](examples/SimpleChat.cs) - Basic chat interface
- [FunctionCalling.cs](examples/FunctionCalling.cs) - Tool/function calling
- [MultiProvider.cs](examples/MultiProvider.cs) - Multiple provider support

## 🤝 Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details.

## 📄 License

This project is licensed under the Apache-2.0 License - see the [LICENSE](LICENSE) file for details.

## 🔗 Related Projects

- [Andy.Configuration](https://www.nuget.org/packages/Andy.Configuration) - Configuration management
- [OpenAI-DotNet](https://github.com/openai/openai-dotnet) - Official OpenAI SDK
- [Cerebras.Cloud.SDK](https://www.nuget.org/packages/Cerebras.Cloud.Sdk.Unofficial) - Cerebras SDK

## 📝 Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.