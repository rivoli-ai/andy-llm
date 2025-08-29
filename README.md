# Andy.Llm

> **⚠️ ALPHA SOFTWARE**: This library is in active development and APIs may change. Not recommended for production use without thorough testing.

A flexible, provider-agnostic .NET library for integrating with Large Language Models (LLMs) through OpenAI-compatible APIs.

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![NuGet](https://img.shields.io/nuget/v/Andy.Llm)](https://www.nuget.org/packages/Andy.Llm/)

## Design Goals

1. **Provider Agnostic**: Support multiple LLM providers through a unified interface
2. **OpenAI Compatibility**: First-class support for OpenAI-compatible APIs
3. **Extensibility**: Easy to add new providers without modifying core logic
4. **Type Safety**: Strongly-typed models and interfaces
5. **Modern .NET**: Built on .NET 8.0 with latest C# features
6. **Production Ready**: Comprehensive error handling, logging, and testing
7. **Resilient**: Built-in retry policies and circuit breakers with Polly
8. **Observable**: Telemetry, metrics, and distributed tracing support
9. **Secure**: API key protection and sensitive data sanitization

## Quick Start

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

## Configuration

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

## Features

### Core Capabilities
- **Multi-Provider Support**: OpenAI, Cerebras, Azure OpenAI, Ollama
- **Streaming Responses**: Real-time token streaming
- **Function/Tool Calling**: OpenAI-compatible function calling
- **Conversation Management**: Context and token limit management
- **Dependency Injection**: Full DI container integration

### Enterprise Features
- **Security**: Secure API key storage with SecureString, sensitive data sanitization
- **Resilience**: Retry policies, circuit breakers, timeout handling via Polly
- **Observability**: Metrics collection, distributed tracing, structured logging (see [Telemetry Guide](docs/TELEMETRY.md))
- **Progress Reporting**: Real-time progress updates for long operations
- **Cancellation Support**: Comprehensive cancellation token support

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

## Architecture

See [ARCHITECTURE.md](docs/ARCHITECTURE.md) for detailed architecture documentation.

## Telemetry and Monitoring

Andy.Llm provides comprehensive telemetry through OpenTelemetry-compatible APIs. See the [Telemetry Guide](docs/TELEMETRY.md) for:
- Metrics collection and export
- Distributed tracing setup
- Integration with Prometheus, Jaeger, Application Insights
- Terminal application monitoring

## Testing

The library includes comprehensive unit and integration tests:

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run only unit tests
dotnet test --filter "Category!=Integration"
```

## 📖 Documentation

- [Getting Started Guide](docs/GETTING_STARTED.md) - Quick setup and basic usage
- [Architecture Documentation](docs/ARCHITECTURE.md) - System design and components
- [API Reference](docs/API_REFERENCE.md) - Complete API documentation
- [Telemetry Guide](docs/TELEMETRY.md) - Monitoring and observability setup
- [Examples](examples/) - Code examples and patterns

## Examples

The [examples](examples/) directory contains complete, runnable projects demonstrating various features:

### Core Examples
- **[SimpleCompletion](examples/SimpleCompletion/)** - Basic text completion with multiple providers
- **[ConversationChat](examples/ConversationChat/)** - Interactive chat with conversation context management
- **[FunctionCalling](examples/FunctionCalling/)** - OpenAI-compatible tool/function calling with weather and calculator examples

### Provider-Specific Examples
- **[AzureOpenAI](examples/AzureOpenAI/)** - Enterprise deployment with Azure OpenAI Service
- **[Ollama](examples/Ollama/)** - Local LLM execution with complete privacy

### Advanced Examples
- **[Streaming](examples/Streaming/)** - Real-time streaming responses with cancellation and progress tracking
- **[MultiProvider](examples/MultiProvider/)** - Comparing responses from multiple LLM providers simultaneously
- **[Telemetry](examples/Telemetry/)** - Metrics collection, distributed tracing, and progress reporting

Run any example with:
```bash
dotnet run --project examples/SimpleCompletion
# Set provider with environment variable
LLM_PROVIDER=cerebras dotnet run --project examples/ConversationChat
```

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details.

## License

Copyright 2025 Rivoli AI

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.

See the [LICENSE](LICENSE) file for the full license text.

### Third-Party Licenses

This project uses the following open-source libraries:
- **OpenAI SDK** - MIT License
- **Cerebras.Cloud.SDK** - Apache-2.0 License  
- **Microsoft.Extensions.Http.Polly** - MIT License
- **Microsoft.Extensions.Logging** - MIT License
- **System.Diagnostics.DiagnosticSource** - MIT License
- **Andy.Configuration** - Apache-2.0 License

All dependencies are compatible with the Apache-2.0 license.

## Security

- API keys are stored securely using SecureString
- Sensitive data is automatically sanitized in logs
- Support for Azure Key Vault and other secret managers
- No hardcoded credentials or secrets

For security concerns, please email security@rivoli-ai.com

## Related Projects

- [Andy.Configuration](https://www.nuget.org/packages/Andy.Configuration) - Configuration management
- [OpenAI-DotNet](https://github.com/openai/openai-dotnet) - Official OpenAI SDK
- [Cerebras.Cloud.SDK](https://www.nuget.org/packages/Cerebras.Cloud.Sdk.Unofficial) - Cerebras SDK

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.