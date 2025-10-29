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

### Structured Output with JSON Schema

```csharp
using Andy.Llm.Models;

// Define a JSON schema for structured output
var schema = @"{
    ""type"": ""object"",
    ""properties"": {
        ""name"": { ""type"": ""string"" },
        ""age"": { ""type"": ""integer"" },
        ""skills"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
    },
    ""required"": [""name"", ""age""]
}";

// Request structured output
var request = new LlmRequest
{
    Messages = new List<Message> 
    { 
        Message.CreateUser("Generate a person profile") 
    },
    ResponseFormat = ResponseFormat.JsonSchema,
    JsonSchema = schema,
    StrictMode = true
};

var response = await client.CompleteAsync(request);
// Response will be valid JSON matching the schema
```

### Hybrid Parsing for Any Response Format

```csharp
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;

// Create a hybrid parser that handles both structured and text responses
var structuredFactory = new StructuredResponseFactory(logger);
var hybridParser = new HybridLlmParser(textParser, structuredFactory, logger);

// Parse any LLM response - OpenAI, Anthropic, or plain text
var ast = hybridParser.Parse(llmResponse);

// Work with the semantic AST
foreach (var node in ast.Children)
{
    switch (node)
    {
        case ToolCallNode toolCall:
            // Handle tool/function calls with structured arguments
            await ExecuteTool(toolCall.ToolName, toolCall.Arguments);
            break;
        case TextNode text:
            // Handle text content
            Console.WriteLine(text.Content);
            break;
        case ErrorNode error:
            // Handle parsing errors gracefully
            logger.LogWarning("Parse error: {Message}", error.Message);
            break;
    }
}
```

### Advanced Usage with Dependency Injection

```csharp
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Configure from environment variables
services.ConfigureLlmFromEnvironment();

// Or configure programmatically with human-readable names
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai/latest-small";
    options.Providers["openai/latest-small"] = new ProviderConfig
    {
        Provider = "openai",
        ApiKey = "your-api-key",
        Model = "gpt-4o-mini",
        ApiBase = "https://api.openai.com/v1"
    };
    options.Providers["openai/latest-large"] = new ProviderConfig
    {
        Provider = "openai",
        ApiKey = "your-api-key",
        Model = "gpt-5",
        ApiBase = "https://api.openai.com/v1",
        Enabled = false
    };
});

var serviceProvider = services.BuildServiceProvider();
var llmClient = serviceProvider.GetRequiredService<LlmClient>();
```

## Configuration

### Configuration Names

The library supports human-readable hierarchical configuration names for better organization:

```json
{
  "Llm": {
    "DefaultProvider": "openai/latest-small",
    "Providers": {
      "openai/latest-large": {
        "Provider": "openai",
        "ApiBase": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-5",
        "Enabled": false
      },
      "openai/latest-small": {
        "Provider": "openai",
        "ApiBase": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-4o-mini",
        "Enabled": true
      },
      "openai/large-code": {
        "Provider": "openai",
        "ApiBase": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-5-codex",
        "Enabled": false
      },
      "cerebras/large-code": {
        "Provider": "cerebras",
        "ApiBase": "https://api.cerebras.ai/v1",
        "ApiKey": "${CEREBRAS_API_KEY}",
        "Model": "qwen-3-coder-480b",
        "Enabled": false
      }
    }
  }
}
```

Configuration names can use any format. The `Provider` property specifies the underlying provider type (openai, cerebras, azure, ollama). If omitted, the provider type is inferred from the configuration name prefix (e.g., "openai/latest-large" infers "openai").

### Provider Priority

The library supports priority-based provider selection using the `Priority` field. When using `CreateAvailableProviderAsync()`, providers are selected in the following order:

1. **Default Provider** (if fully configured and enabled)
2. **Providers with Priority** (ordered from highest to lowest)
3. **Remaining Enabled Providers** (in dictionary order)

A provider is considered "fully configured" when all required fields are present and non-empty:
- **OpenAI/Cerebras**: `ApiKey`, `ApiBase`, and `Model` must be set
- **Azure**: `ApiKey`, `ApiBase`, and `DeploymentName` must be set
- **Ollama**: `ApiBase` and `Model` must be set (no API key required)

Example with priorities:

```json
{
  "Llm": {
    "DefaultProvider": "openai/latest-large",
    "Providers": {
      "openai/latest-large": {
        "Provider": "openai",
        "ApiBase": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-4o",
        "Enabled": true,
        "Priority": 100
      },
      "cerebras/fast-large": {
        "Provider": "cerebras",
        "ApiBase": "https://api.cerebras.ai/v1",
        "ApiKey": "${CEREBRAS_API_KEY}",
        "Model": "llama-3.3-70b",
        "Enabled": true,
        "Priority": 90
      },
      "openai/latest-small": {
        "Provider": "openai",
        "ApiBase": "https://api.openai.com/v1",
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "gpt-4o-mini",
        "Enabled": true,
        "Priority": 80
      }
    }
  }
}
```

In this example:
- If `OPENAI_API_KEY` is set, `openai/latest-large` will be used (default provider, Priority 100)
- If `OPENAI_API_KEY` is not set but `CEREBRAS_API_KEY` is set, `cerebras/fast-large` will be used (Priority 90)
- If only `openai/latest-small` is configured, it will be used (Priority 80)

Providers without the `Priority` field have no explicit priority and are selected after all prioritized providers.

### Backward Compatibility

Simple configuration names without hierarchical structure remain supported:

```csharp
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
    options.Providers["openai"] = new ProviderConfig
    {
        ApiKey = "your-api-key",
        Model = "gpt-4o-mini"
    };
});
```

### Environment Variables

The library supports configuration through environment variables for all major providers:

#### OpenAI
- `OPENAI_API_KEY` - Your OpenAI API key
- `OPENAI_API_BASE` - API base URL (required)
- `OPENAI_MODEL` - Model to use (required)
- `OPENAI_ORGANIZATION` - Organization ID (optional)

#### Cerebras
- `CEREBRAS_API_KEY` - Your Cerebras API key
- `CEREBRAS_API_BASE` - API base URL (required)
- `CEREBRAS_MODEL` - Model to use (required)

#### Azure OpenAI
- `AZURE_OPENAI_ENDPOINT` - Your Azure OpenAI endpoint
- `AZURE_OPENAI_KEY` - Your Azure OpenAI key
- `AZURE_OPENAI_DEPLOYMENT` - Your deployment name
- `AZURE_OPENAI_API_VERSION` - API version (default: 2024-02-15-preview)

#### Local/Ollama
- `OLLAMA_API_BASE` - Your local endpoint (required, e.g., http://localhost:11434)
- `OLLAMA_MODEL` - Model to use (required, e.g., llama2)

## Features

### Core Capabilities
- **Multi-Provider Support**: OpenAI, Cerebras, Azure OpenAI, Ollama, Anthropic
- **Streaming Responses**: Real-time token streaming
- **Function/Tool Calling**: OpenAI-compatible function calling with structured outputs
- **Structured Outputs**: JSON Schema validation and type-safe responses
- **Hybrid Parsing**: Automatic detection and parsing of structured vs text responses
- **AST Generation**: Semantic Abstract Syntax Tree for all LLM responses
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

### Tool Calling with Structured Outputs

```csharp
// Define tools with JSON schemas
var tools = new List<ToolDeclaration>
{
    new ToolDeclaration
    {
        Name = "get_weather",
        Description = "Get weather for a location",
        Parameters = new ToolParameters
        {
            Type = "object",
            Properties = new Dictionary<string, ParameterSchema>
            {
                ["location"] = new ParameterSchema { Type = "string" },
                ["units"] = new ParameterSchema 
                { 
                    Type = "string", 
                    Enum = new[] { "celsius", "fahrenheit" } 
                }
            },
            Required = new[] { "location" }
        }
    }
};

// Request with tool calling
var request = new LlmRequest
{
    Messages = messages,
    Tools = tools,
    ToolChoice = ToolChoice.Auto // Let model decide when to use tools
};

// Parse response with automatic tool detection
var response = await client.CompleteAsync(request);
var ast = hybridParser.Parse(response.Content);

// Execute tool calls from AST
foreach (var toolCall in ast.Children.OfType<ToolCallNode>())
{
    if (toolCall.ParseError == null)
    {
        var result = await ExecuteTool(toolCall.ToolName, toolCall.Arguments);
        // Send result back to LLM for final response
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

### Recent API Additions

- `LlmRequest.Functions`: alias of `Tools` for function/tool declaration.
- `FunctionCall.ArgumentsJson`: preserves raw JSON arguments from providers.
- `LlmStreamResponse.FinishReason`: reason for stream completion on final chunk.

Streaming now emits partial function-call deltas when providers stream arguments, and marks the final chunk with `IsComplete = true` and `FinishReason`.

See `docs/implementation.md` for implementation details and current status.

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

// Use specific configuration by name
var smallModel = factory.CreateProvider("openai/latest-small");
var largeModel = factory.CreateProvider("openai/latest-large");
var codeModel = factory.CreateProvider("cerebras/large-code");

// Backward compatible: simple provider names still work
var openAiProvider = factory.CreateProvider("openai");
var cerebrasProvider = factory.CreateProvider("cerebras");

// Get first available provider based on priority and completeness
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
- **[StructuredOutput](examples/StructuredOutput/)** - JSON Schema validation and structured responses
- **[HybridParsing](examples/HybridParsing/)** - Automatic detection and parsing of different response formats
- **[ToolCallingStructured](examples/ToolCallingStructured/)** - Advanced tool calling with schema validation

Run any example with:
```bash
dotnet run --project examples/SimpleCompletion
# Set provider with environment variable
LLM_PROVIDER=cerebras dotnet run --project examples/ConversationChat
```

## Response Parsing and AST

Andy.Llm includes a sophisticated parsing system that creates a semantic Abstract Syntax Tree (AST) from any LLM response:

### AST Node Types

- **ResponseNode**: Root node with metadata (provider, model, tokens)
- **TextNode**: Plain or formatted text content
- **ToolCallNode**: Function/tool invocations with parsed arguments
- **ToolResultNode**: Results from tool execution
- **CodeNode**: Code blocks with language detection
- **ErrorNode**: Parsing or validation errors
- **FileReferenceNode**: File paths and references
- **MarkdownNode**: Structured markdown elements

### Visitor Pattern Support

```csharp
public class CustomVisitor : IAstVisitor<string>
{
    public string VisitToolCall(ToolCallNode node)
    {
        // Process tool calls
        return $"Tool: {node.ToolName}";
    }
    
    public string VisitText(TextNode node)
    {
        // Process text content
        return node.Content;
    }
    // ... other visit methods
}

// Apply visitor to AST
var ast = parser.Parse(response);
var result = ast.Accept(new CustomVisitor());
```

### Provider Detection

The hybrid parser automatically detects response formats:
- OpenAI format (choices, tool_calls)
- Anthropic format (content blocks, tool_use)
- Server-Sent Events (SSE)
- JSON Lines (JSONL)
- Plain text with embedded JSON

### Error Handling

All parsing errors are captured in the AST:

```csharp
foreach (var node in ast.Children.OfType<ToolCallNode>())
{
    if (node.ParseError != null)
    {
        // Handle malformed arguments
        logger.LogWarning("Tool arguments invalid: {Error}", 
            node.ParseError.Message);
        // Access raw JSON for manual processing
        var raw = node.Metadata["RawArgumentsJson"];
    }
}
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