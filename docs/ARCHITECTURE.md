# Andy.Llm Architecture Documentation

## Table of Contents
1. [Overview](#overview)
2. [Design Principles](#design-principles)
3. [Core Components](#core-components)
4. [Provider Architecture](#provider-architecture)
5. [Message Flow](#message-flow)
6. [Extension Points](#extension-points)
7. [Design Decisions](#design-decisions)

## Overview

Andy.Llm is designed as a provider-agnostic LLM integration library that maintains compatibility with OpenAI's API while supporting multiple LLM providers. The architecture follows SOLID principles and uses dependency injection throughout for flexibility and testability.

```
┌─────────────────────────────────────────────────────────────┐
│                        Application                           │
└─────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                         LlmClient                            │
│  • Legacy API compatibility                                  │
│  • High-level convenience methods                            │
│  • Provider abstraction                                      │
└─────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                    ILlmProviderFactory                       │
│  • Provider selection                                        │
│  • Fallback logic                                           │
│  • Caching                                                  │
└─────────────────────────────────────────────────────────────┘
                                │
        ┌───────────────┬───────────────┬───────────────┐
        ▼               ▼               ▼               ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│   OpenAI     │ │   Cerebras   │ │    Azure     │ │   Ollama     │
│  Provider    │ │   Provider   │ │   OpenAI     │ │   Provider   │
│              │ │              │ │   Provider   │ │              │
│ • OpenAI SDK │ │ • Cerebras   │ │ • Azure SDK  │ │ • HTTP API   │
│ • Streaming  │ │   SDK        │ │ • Deployment │ │ • Local LLM  │
│ • Functions  │ │ • Fast       │ │   based     │ │ • No cloud   │
│ • GPT-4/3.5  │ │   inference  │ │ • Enterprise │ │ • Privacy    │
└──────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
        │               │               │               │
        └───────────────┴───────────────┴───────────────┘
                                │
                    ┌───────────────────────┐
                    │   External Services   │
                    │ • OpenAI API          │
                    │ • Cerebras Cloud      │
                    │ • Azure OpenAI        │
                    │ • Local Ollama        │
                    └───────────────────────┘
```

## Design Principles

### 1. Provider Agnostic Design
The core interfaces (`ILlmProvider`, `ILlmProviderFactory`) define contracts that any LLM provider must implement, ensuring consistent behavior across different services.

### 2. Dependency Injection First
All components are designed for dependency injection, making the library highly testable and configurable.

### 3. Immutable Messages
Message objects are designed to be immutable once created, ensuring thread safety and preventing accidental modifications.

### 4. Streaming as First-Class Citizen
Both streaming and non-streaming APIs are treated equally, with providers implementing both patterns efficiently.

### 5. Configuration Flexibility
Support for multiple configuration sources: environment variables, appsettings.json, and programmatic configuration.

## Core Components

### ILlmProvider Interface

```csharp
public interface ILlmProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
    IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
    string Name { get; }
}
```

**Purpose**: Defines the contract for all LLM providers.

**Key Responsibilities**:
- Execute completion requests
- Stream responses
- Check availability
- Provider identification

### LlmClient

**Purpose**: Main entry point for consumers, providing both legacy compatibility and modern APIs.

**Key Features**:
- Multiple constructor overloads for different initialization patterns
- Automatic provider selection through factory
- Simplified API for common use cases
- Full access to advanced features

### ILlmProviderFactory

```csharp
public interface ILlmProviderFactory
{
    ILlmProvider CreateProvider(string? providerName = null);
    Task<ILlmProvider> CreateAvailableProviderAsync(CancellationToken ct);
}
```

**Purpose**: Creates and manages provider instances.

**Key Responsibilities**:
- Provider instantiation
- Caching provider instances
- Fallback logic when providers are unavailable
- Provider availability checking

### Message System

The message system uses a composite pattern to represent complex message structures:

```csharp
Message
├── Role (System, User, Assistant, Tool)
└── Parts[]
    ├── TextPart
    ├── ToolCallPart
    └── ToolResponsePart
```

**Benefits**:
- Flexible message composition
- Support for multi-modal content (future)
- Clear separation of concerns
- Easy serialization/deserialization

### ConversationContext

**Purpose**: Manages conversation state and context window.

**Key Features**:
- Automatic context pruning based on limits
- System instruction management
- Tool declaration management
- Request generation from context

## Provider Architecture

### Provider Implementation Pattern

Each provider follows this pattern:

1. **Configuration Loading**: Read provider-specific settings from `ProviderConfig`
2. **Client Initialization**: Create SDK clients or HTTP clients with proper options
3. **API Strategy Selection**: Choose the correct API protocol (e.g., Chat Completions vs Responses)
4. **Request Translation**: Convert generic requests to provider-specific format
5. **Response Translation**: Convert provider responses to generic format
6. **Error Handling**: Wrap provider-specific errors in consistent exceptions

### Multi-Endpoint Support (Strategy Pattern)

OpenAI now provides multiple API endpoints with different protocols. The provider uses a **strategy pattern** to handle this:

```
┌─────────────────────────────────────────────────────────┐
│                     OpenAIProvider                        │
│  • Shared: auth, HTTP client, model metadata             │
│  • Delegates to strategy for API calls                   │
└───────────────────────┬─────────────────────────────────┘
                        │
              ┌─────────┴─────────┐
              ▼                   ▼
┌──────────────────────┐ ┌──────────────────────┐
│ ChatCompletionsStrategy│ │ ResponsesApiStrategy │
│                      │ │                      │
│ • /v1/chat/completions│ │ • /v1/responses      │
│ • OpenAI SDK         │ │ • HttpClient direct  │
│ • GPT-4, GPT-4o     │ │ • Codex models       │
│ • Standard models    │ │ • Built-in tools     │
└──────────────────────┘ └──────────────────────┘
```

**Strategy Selection** is determined by:
1. Explicit `ProviderConfig.ApiType` setting (`"chat-completions"` or `"responses"`)
2. Auto-detection from model name (models containing "codex" use Responses API)
3. Default: Chat Completions API

### Compound Provider Aliases

The factory supports **compound provider aliases** like `"openai/codex-mini"`:

```json
{
  "Llm": {
    "DefaultProvider": "openai/codex-mini",
    "Providers": {
      "openai": { "Provider": "openai", "Model": "gpt-4o" },
      "openai/codex-mini": { "Provider": "openai", "Model": "codex-mini-latest", "ApiType": "responses" },
      "openai/codex-5.1": { "Provider": "openai", "Model": "gpt-5.1-codex" }
    }
  }
}
```

- The dictionary **key** is the alias (e.g., `"openai/codex-mini"`)
- The `Provider` field indicates the underlying provider **type** (e.g., `"openai"`)
- Each alias gets its own cached provider instance with separate model/config

### ProviderConfig Fields

```csharp
public class ProviderConfig
{
    public string? Provider { get; set; }      // Provider type: "openai", "anthropic", etc.
    public string? ApiType { get; set; }       // API protocol: "chat-completions", "responses"
    public string? ApiKey { get; set; }        // Authentication key
    public string? ApiBase { get; set; }       // Base URL for the API
    public string? Model { get; set; }         // Default model for this config
    public string? Organization { get; set; }  // OpenAI organization
    public string? ApiVersion { get; set; }    // Azure API version
    public string? DeploymentName { get; set; }// Azure deployment name
    public bool Enabled { get; set; }          // Whether this provider is active
}
```

### Current Provider Implementations

1. **OpenAIProvider**: Supports both Chat Completions and Responses API via strategy pattern
2. **CerebrasProvider**: Fast inference using Cerebras Cloud SDK (OpenAI-compatible)
3. **AzureOpenAIProvider**: Enterprise Azure OpenAI with deployment-based access
4. **OllamaProvider**: Local LLM execution via HTTP API

### Adding New Providers

To add a new provider:

1. Implement `ILlmProvider` interface
2. Register in DI container (`ServiceCollectionExtensions`)
3. Add to the factory's `ResolveFromDI()` and `CreateProviderInstance()` methods
4. Add the provider type name to `KnownProviderTypes`
5. Add configuration mapping in `ConfigureLlmFromEnvironment`

### Adding New API Strategies

To add a new API protocol for an existing provider (e.g., a future OpenAI Agents API):

1. Implement `IOpenAIApiStrategy` interface
2. Add the new `ApiType` value to `ProviderConfig` documentation
3. Update `OpenAIProvider.CreateStrategy()` to handle the new type
4. Update `DetectApiType()` if auto-detection is needed

## Message Flow

### Synchronous Completion Flow

```
User Code → LlmClient.CompleteAsync()
    → ILlmProvider.CompleteAsync()
        → Provider converts request
        → Provider calls external API
        → Provider converts response
    ← Returns LlmResponse
← Returns to user
```

### Streaming Flow

```
User Code → LlmClient.StreamCompleteAsync()
    → ILlmProvider.StreamCompleteAsync()
        → Provider converts request
        → Provider opens stream to API
        → For each chunk:
            → Convert chunk
            → Yield LlmStreamResponse
    ← Async enumerable
← Stream to user
```

### Function Calling Flow

```
1. User adds tool declarations to context
2. User sends message
3. Provider includes tools in request
4. LLM returns function calls
5. User executes functions
6. User adds results to context
7. User sends follow-up request
8. LLM provides final response
```

## Extension Points

### Custom Providers

Implement `ILlmProvider` to add support for new LLM services:

```csharp
public class CustomProvider : ILlmProvider
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        // Your implementation
    }
}
```

### Custom Message Parts

Extend `MessagePart` for new content types:

```csharp
public class ImagePart : MessagePart
{
    public byte[] ImageData { get; set; }
    public string MimeType { get; set; }
    
    public override int GetCharacterCount() => ImageData.Length;
}
```

### Configuration Sources

Add custom configuration sources:

```csharp
services.Configure<LlmOptions>(options =>
{
    options.Providers["custom"] = LoadFromCustomSource();
});
```

## Design Decisions

### Why Provider Factory Pattern?

**Decision**: Use factory pattern for provider creation.

**Rationale**:
- Centralizes provider instantiation logic
- Enables caching of provider instances
- Supports dynamic provider selection
- Simplifies fallback logic

### Why Separate Request/Response Models?

**Decision**: Use separate models instead of provider-specific ones.

**Rationale**:
- Maintains provider independence
- Simplifies consumer code
- Enables cross-provider compatibility
- Reduces coupling

### Why ConversationContext?

**Decision**: Provide conversation management as separate concern.

**Rationale**:
- Many use cases need conversation tracking
- Context window management is complex
- Reduces boilerplate in consumer code
- Enables advanced features (summarization, pruning)

### Why Support Legacy API?

**Decision**: Maintain backward compatibility with simple constructor.

**Rationale**:
- Smooth migration path
- Supports simple use cases
- Reduces learning curve
- Maintains familiarity for OpenAI SDK users

### Why Streaming and Non-Streaming?

**Decision**: Support both streaming and synchronous APIs.

**Rationale**:
- Different use cases require different patterns
- Streaming essential for real-time experiences
- Synchronous simpler for batch processing
- Matches provider capabilities

## Performance Considerations

### Provider Caching
Providers are cached after first creation to avoid repeated initialization overhead.

### Message Buffering
Streaming responses use efficient buffering to minimize memory allocations.

### Context Pruning
Automatic context pruning prevents excessive token usage and API costs.

### Lazy Initialization
Providers are initialized only when first requested, reducing startup time.

## Security Considerations

### API Key Management
- Support for environment variables
- No hardcoded keys in code
- Secure configuration sources

### Request Validation
- Input sanitization
- Parameter validation
- Rate limiting support (provider-dependent)

### Error Handling
- No sensitive data in error messages
- Proper exception wrapping
- Audit logging capability

## Future Enhancements

### Planned Features
1. **Multi-modal Support**: Images, audio, video
2. **Embedding APIs**: Vector generation and similarity
3. **Fine-tuning Support**: Model customization APIs
4. **Caching Layer**: Response caching for identical requests
5. **Retry Logic**: Automatic retry with exponential backoff
6. **Metrics Collection**: Performance and usage metrics
7. **Provider Health Checks**: Automated provider monitoring

### Extension Areas
1. **Message Compression**: Reduce token usage
2. **Context Summarization**: Intelligent context pruning
3. **Provider Routing**: Smart routing based on request type
4. **Cost Optimization**: Route to cheapest capable provider
5. **Response Validation**: Ensure response quality
6. **New API Strategies**: Support future API protocols (Agents API, etc.)
7. **Additional Providers**: Anthropic, Google, Groq, Qwen, router APIs

## Testing Strategy

### Unit Testing
- Mock providers for isolated testing
- Test message conversion logic
- Test configuration loading
- Test factory behavior

### Integration Testing
- Real provider testing with environment variables
- Streaming behavior verification
- Function calling end-to-end
- Error handling scenarios

### Performance Testing
- Streaming throughput
- Memory usage during streaming
- Provider initialization time
- Context pruning efficiency