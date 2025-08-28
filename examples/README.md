# Andy.Llm Examples

This directory contains complete, runnable examples demonstrating various features of the Andy.Llm library.

## Running Examples

All examples require API keys to be configured via environment variables. See the main [README](../README.md#configuration) for provider-specific configuration.

```bash
# OpenAI (default for most examples)
export OPENAI_API_KEY="your-key-here"
dotnet run --project SimpleCompletion

# Cerebras
export CEREBRAS_API_KEY="your-key-here"
export LLM_PROVIDER=cerebras
dotnet run --project ConversationChat

# Azure OpenAI
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export AZURE_OPENAI_KEY="your-key-here"
export AZURE_OPENAI_DEPLOYMENT="your-deployment"
dotnet run --project FunctionCalling
```

## Examples Overview

### SimpleCompletion
Demonstrates basic text completion with OpenAI and Cerebras providers. Shows how to:
- Configure providers from environment variables
- Make simple completion requests
- Handle provider-specific configurations
- Stream responses

### ConversationChat
Interactive chat application showcasing:
- Conversation context management
- System instructions
- Message history pruning
- Token usage tracking
- Provider switching (OpenAI/Cerebras)

### FunctionCalling
OpenAI-compatible function/tool calling example featuring:
- Weather checking tool
- Calculator tool
- Tool response handling
- Multi-step conversations with tools

### Streaming
Advanced streaming example demonstrating:
- Basic streaming with progress display
- Cancellation support
- Error handling with retries
- Function calls in streaming mode
- Live statistics during streaming

### MultiProvider
Compares responses from multiple providers:
- Parallel requests to different providers
- Response comparison
- Provider availability checking
- Configuration management

### Telemetry
Comprehensive telemetry and monitoring example:
- OpenTelemetry metrics collection
- Distributed tracing
- Custom metrics listeners
- Progress reporting
- Console exporters for metrics and traces

## Common Patterns

### Error Handling
All examples include proper error handling for common scenarios:
- Missing API keys
- Network failures
- Rate limiting
- Invalid responses

### Configuration
Examples use the `ConfigureLlmFromEnvironment()` extension method for automatic configuration:
```csharp
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    options.DefaultProvider = "openai";
});
```

### Dependency Injection
All examples use Microsoft.Extensions.DependencyInjection for proper service configuration and lifetime management.

## Extending Examples

Feel free to modify these examples for your use cases. Common modifications:
- Add new tools/functions
- Implement custom providers
- Add database persistence
- Integrate with existing applications
- Add custom telemetry exporters