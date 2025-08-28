# Andy.Llm API Reference

## Namespaces

- `Andy.Llm` - Main client and core functionality
- `Andy.Llm.Models` - Request/response models and message types
- `Andy.Llm.Abstractions` - Interfaces for providers
- `Andy.Llm.Configuration` - Configuration models
- `Andy.Llm.Providers` - Provider implementations
- `Andy.Llm.Services` - Factory and service classes
- `Andy.Llm.Extensions` - Extension methods for DI
- `Andy.Llm.Security` - API key protection and data sanitization
- `Andy.Llm.Telemetry` - Metrics and distributed tracing
- `Andy.Llm.Resilience` - Retry policies and circuit breakers
- `Andy.Llm.Progress` - Progress reporting and cancellation

## Core Classes

### LlmClient

Main client for interacting with LLM providers.

#### Constructors

```csharp
// Simple API key constructor
public LlmClient(string apiKey)

// OpenAI client constructor (legacy compatibility)
public LlmClient(OpenAIClient openAiClient)

// Provider factory constructor (recommended)
public LlmClient(ILlmProviderFactory providerFactory, ILogger<LlmClient> logger)

// Direct provider constructor
public LlmClient(ILlmProvider provider, ILogger<LlmClient> logger)
```

#### Methods

```csharp
// Get a simple text response
Task<string> GetResponseAsync(string message, string model = "gpt-4", CancellationToken ct = default)

// Complete a request
Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)

// Stream a response
IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken ct = default)

// Legacy OpenAI compatibility
Task<ChatCompletion> GetChatCompletionAsync(IEnumerable<ChatMessage> messages, string model = "gpt-4", CancellationToken ct = default)
IAsyncEnumerable<StreamingChatCompletionUpdate> GetChatCompletionStreamAsync(IEnumerable<ChatMessage> messages, string model = "gpt-4", CancellationToken ct = default)
ChatClient? GetChatClient(string model = "gpt-4o")
```

### ConversationContext

Manages conversation state and history.

#### Properties

```csharp
// Current messages in context
IReadOnlyList<Message> Messages { get; }

// Complete history including pruned messages
IReadOnlyList<Message> ComprehensiveHistory { get; }

// System instruction/prompt
string? SystemInstruction { get; set; }

// Available tools for function calling
List<ToolDeclaration> AvailableTools { get; set; }

// Maximum messages to keep in context
int MaxContextMessages { get; set; } = 50

// Maximum character count for context
int MaxContextCharacters { get; set; } = 100000
```

#### Methods

```csharp
// Add messages
void AddUserMessage(string content)
void AddAssistantMessage(string content)
void AddAssistantMessageWithToolCalls(string? content, List<FunctionCall> functionCalls)
void AddToolResponse(string toolName, string callId, object response)

// Context management
LlmRequest CreateRequest(string? model = null)
int GetCharacterCount()
void Clear()
string GetSummary()
```

## Models

### LlmRequest

Request to send to an LLM provider.

```csharp
public class LlmRequest
{
    public List<Message> Messages { get; set; }
    public List<ToolDeclaration>? Tools { get; set; }
    public string? Model { get; set; }
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public string? SystemPrompt { get; set; }
    public bool Stream { get; set; }
}
```

### LlmResponse

Complete response from an LLM provider.

```csharp
public class LlmResponse
{
    public string Content { get; set; }
    public List<FunctionCall> FunctionCalls { get; set; }
    public string? FinishReason { get; set; }
    public int? TokensUsed { get; set; }
    public string? Model { get; set; }
    public TokenUsage? Usage { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class TokenUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}
```

### LlmStreamResponse

Streaming response chunk.

```csharp
public class LlmStreamResponse
{
    public string? TextDelta { get; set; }
    public FunctionCall? FunctionCall { get; set; }
    public bool IsComplete { get; set; }
    public string? Error { get; set; }
}
```

### Message

Represents a message in the conversation.

```csharp
public class Message
{
    public required MessageRole Role { get; set; }
    public List<MessagePart> Parts { get; set; }
    
    // Helper method
    public static Message CreateText(MessageRole role, string content)
    public int GetCharacterCount()
}

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}
```

### MessagePart

Base class for message parts.

```csharp
// Text content
public class TextPart : MessagePart
{
    public required string Text { get; set; }
}

// Tool call from assistant
public class ToolCallPart : MessagePart
{
    public required string ToolName { get; set; }
    public required string CallId { get; set; }
    public Dictionary<string, object?> Arguments { get; set; }
}

// Tool response
public class ToolResponsePart : MessagePart
{
    public required string ToolName { get; set; }
    public required string CallId { get; set; }
    public object? Response { get; set; }
}
```

### ToolDeclaration

Declares a tool/function available to the LLM.

```csharp
public class ToolDeclaration
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
    public bool Required { get; set; }
}
```

### FunctionCall

Represents a function call request from the LLM.

```csharp
public class FunctionCall
{
    public required string Name { get; set; }
    public required string Id { get; set; }
    public Dictionary<string, object?> Arguments { get; set; }
}
```

## Configuration

### LlmOptions

Global configuration options.

```csharp
public class LlmOptions
{
    public string DefaultProvider { get; set; } = "openai";
    public Dictionary<string, ProviderConfig> Providers { get; set; }
    public string? DefaultModel { get; set; }
    public double DefaultTemperature { get; set; } = 0.7;
    public int DefaultMaxTokens { get; set; } = 4096;
    public bool EnableCaching { get; set; } = true;
    public int CacheDurationMinutes { get; set; } = 60;
}
```

### ProviderConfig

Provider-specific configuration.

```csharp
public class ProviderConfig
{
    public string? ApiKey { get; set; }
    public string? ApiBase { get; set; }
    public string? Model { get; set; }
    public string? Organization { get; set; }
    public string? ApiVersion { get; set; }
    public string? DeploymentName { get; set; }
    public Dictionary<string, object>? AdditionalSettings { get; set; }
    public bool Enabled { get; set; } = true;
}
```

## Interfaces

### ILlmProvider

Interface for LLM provider implementations.

```csharp
public interface ILlmProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    string Name { get; }
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
```

### ILlmProviderFactory

Factory for creating provider instances.

```csharp
public interface ILlmProviderFactory
{
    ILlmProvider CreateProvider(string? providerName = null);
    Task<ILlmProvider> CreateAvailableProviderAsync(CancellationToken cancellationToken = default);
}
```

## Extension Methods

### ServiceCollectionExtensions

DI registration extensions.

```csharp
// Add LLM services with configuration
IServiceCollection AddLlmServices(this IServiceCollection services, IConfiguration configuration)
IServiceCollection AddLlmServices(this IServiceCollection services, Action<LlmOptions> configure)

// Add custom provider
IServiceCollection AddLlmProvider<TProvider>(this IServiceCollection services) where TProvider : class, ILlmProvider

// Configure from environment variables
IServiceCollection ConfigureLlmFromEnvironment(this IServiceCollection services)
```

## Security

### SecureApiKeyProvider

Securely stores API keys using SecureString.

```csharp
public class SecureApiKeyProvider : IDisposable
{
    void SetApiKey(string provider, string apiKey)
    string? GetApiKey(string provider)
    bool HasApiKey(string provider)
    void RemoveApiKey(string provider)
    void Clear()
}
```

### SensitiveDataSanitizer

Sanitizes sensitive data from logs and outputs.

```csharp
public static class SensitiveDataSanitizer
{
    static string Sanitize(string? input)
    static string MaskApiKey(string? apiKey, int visibleChars = 4)
    static bool ContainsSensitiveData(string? input)
    static string SanitizeException(Exception? exception)
}
```

## Telemetry

### LlmMetrics

Collects OpenTelemetry-compatible metrics.

```csharp
public class LlmMetrics : IDisposable
{
    void RecordRequest(string provider, string model, string operation)
    void RecordTokens(string provider, string model, int promptTokens, int completionTokens)
    void RecordLatency(string provider, string model, double latencyMs, bool success = true)
    void RecordError(string provider, string model, string errorType)
    void RecordRetry(string provider, int attemptNumber)
    void RecordTimeout(string provider, string phase)
}
```

### TelemetryMiddleware

Wraps operations with telemetry collection.

```csharp
public class TelemetryMiddleware
{
    Task<T> ExecuteWithTelemetryAsync<T>(
        string provider,
        string model,
        string operationName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
}
```

## Resilience

### ResiliencePolicies

Provides Polly-based resilience policies.

```csharp
public static class ResiliencePolicies
{
    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(
        ILogger? logger = null,
        int maxRetryAttempts = 3)
    
    static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
        ILogger? logger = null,
        int handledEventsAllowedBeforeBreaking = 3,
        TimeSpan? durationOfBreak = null)
    
    static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(
        TimeSpan? timeout = null)
    
    static IAsyncPolicy<HttpResponseMessage> GetCombinedPolicy(
        ILogger? logger = null,
        ResilienceOptions? options = null)
}
```

### ResilienceOptions

```csharp
public class ResilienceOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public int CircuitBreakerThreshold { get; set; } = 3;
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public bool EnableRetry { get; set; } = true;
    public bool EnableCircuitBreaker { get; set; } = true;
    public bool EnableTimeout { get; set; } = true;
}
```

## Progress

### LlmProgressReporter

Reports operation progress.

```csharp
public class LlmProgressReporter
{
    public LlmProgressReporter(Action<LlmOperationProgress> onProgress)
    
    void ReportStart(string operationType)
    void ReportPhase(string phase, int percentComplete, string? message = null)
    void ReportTokens(int tokensProcessed, int? estimatedTotal = null)
    void ReportCompletion(string? message = null)
    void ReportError(string errorMessage)
}
```

### LlmOperationProgress

```csharp
public class LlmOperationProgress
{
    public string OperationType { get; set; }
    public string Phase { get; set; }
    public int PercentComplete { get; set; }
    public string? Message { get; set; }
    public int TokensProcessed { get; set; }
    public int? EstimatedTotalTokens { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
}
```

## Providers

### OpenAIProvider

Provider for OpenAI API and compatible services.

**Supported Features:**
- Text completion
- Streaming
- Function calling
- Multiple models
- Custom endpoints

**Configuration:**
```csharp
{
    "ApiKey": "sk-...",
    "Model": "gpt-4o",
    "ApiBase": "https://api.openai.com/v1", // optional
    "Organization": "org-..." // optional
}
```

### CerebrasProvider

Provider for Cerebras Cloud API using OpenAI-compatible endpoint.

**Supported Features:**
- Text completion
- Streaming  
- Function calling (model-dependent, may not be supported)
- Fast inference with Llama models

**Configuration:**
```csharp
{
    "ApiKey": "your-cerebras-key",
    "ApiBase": "https://api.cerebras.ai/v1", // Default endpoint
    "Model": "llama3.1-70b" // Or llama3.1-8b for faster responses
}
```

**Available Models:**
- `llama3.1-8b` - Fast, efficient for simple tasks (default)
- `llama3.1-70b` - More capable, better for complex tasks (may require higher tier access)

**Note:** Cerebras uses an OpenAI-compatible API, so it works with the OpenAI SDK. Function calling support depends on the model capabilities.

### AzureOpenAIProvider

Provider for Azure OpenAI Service with enterprise features.

**Supported Features:**
- Text completion
- Streaming
- Function calling
- Deployment-based model access
- Enterprise security and compliance
- VNet integration and private endpoints

**Configuration:**
```csharp
{
    "ApiKey": "your-azure-key",
    "ApiBase": "https://your-resource.openai.azure.com",
    "DeploymentName": "gpt-4", // Your deployment name
    "ApiVersion": "2024-02-15-preview", // API version
    "Model": "gpt-4" // Model identifier
}
```

**Environment Variables:**
- `AZURE_OPENAI_ENDPOINT` - Your Azure OpenAI endpoint
- `AZURE_OPENAI_KEY` - Your API key
- `AZURE_OPENAI_DEPLOYMENT` - Your deployment name
- `AZURE_OPENAI_API_VERSION` - API version (optional)

**Key Differences from OpenAI:**
- Uses deployment names instead of model names
- Requires Azure subscription
- Offers enterprise compliance (SOC 2, ISO 27001, HIPAA)
- Data stays in your subscription
- Built-in content filtering

### OllamaProvider

Provider for Ollama local LLM server.

**Supported Features:**
- Text completion
- Streaming
- Local model execution
- No API key required
- Complete privacy

**Configuration:**
```csharp
{
    "ApiBase": "http://localhost:11434", // Ollama server URL
    "Model": "llama2" // Local model name
}
```

**Environment Variables:**
- `OLLAMA_API_BASE` - Server URL (default: http://localhost:11434)
- `OLLAMA_MODEL` - Model to use (default: llama2)

**Available Models (examples):**
- `llama2` - Meta's Llama 2
- `mistral` - Mistral 7B
- `codellama` - Code-specialized model
- `phi` - Microsoft's Phi-2
- Custom models via Modelfile

**Installation:**
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a model
ollama pull llama2

# Start server
ollama serve
```

## Error Handling

### Common Exceptions

```csharp
// Configuration errors
InvalidOperationException - Missing configuration or provider not found

// API errors (from OpenAI SDK)
ClientResultException - API request failed (401, 429, 500, etc.)

// Argument errors
ArgumentNullException - Null argument provided
ArgumentException - Invalid argument value

// Timeout errors
TaskCanceledException - Request timeout or cancellation
```

### Error Response Handling

```csharp
try
{
    var response = await client.CompleteAsync(request);
}
catch (ClientResultException ex) when (ex.Status == 401)
{
    // Invalid API key
}
catch (ClientResultException ex) when (ex.Status == 429)
{
    // Rate limit exceeded
}
catch (ClientResultException ex) when (ex.Status >= 500)
{
    // Server error
}
```

## Examples

### Basic Completion

```csharp
var client = new LlmClient("api-key");
var response = await client.GetResponseAsync("Hello!");
```

### Streaming with Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

await foreach (var chunk in client.StreamCompleteAsync(request, cts.Token))
{
    Console.Write(chunk.TextDelta);
}
```

### Function Calling

```csharp
var request = new LlmRequest
{
    Messages = new() { Message.CreateText(MessageRole.User, "What's the weather?") },
    Tools = new()
    {
        new ToolDeclaration
        {
            Name = "get_weather",
            Description = "Get weather for location",
            Parameters = new()
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["location"] = new { type = "string" }
                }
            }
        }
    }
};

var response = await client.CompleteAsync(request);
foreach (var call in response.FunctionCalls)
{
    // Handle function call
}
```

### Provider Selection

```csharp
var factory = services.GetRequiredService<ILlmProviderFactory>();

// Explicit provider
var openai = factory.CreateProvider("openai");

// First available
var provider = await factory.CreateAvailableProviderAsync();
```