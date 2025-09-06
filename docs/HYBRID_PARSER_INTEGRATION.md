# Hybrid LLM Parser Integration Guide

## Overview
This document outlines the comprehensive requirements, implementation details, and integration steps for the HybridLlmParser system in the andy-llm package. This parser provides a unified interface for handling both structured API responses (OpenAI, Anthropic) and text-based responses (Qwen, raw text models).

## Architecture

### Core Components

#### 1. HybridLlmParser (`/src/Andy.Llm/Parsing/HybridLlmParser.cs`)
- **Purpose**: Main parser that attempts structured parsing first, falls back to text parsing
- **Key Features**:
  - Automatic detection of structured vs text responses
  - Safe error handling with fallback mechanisms
  - Unified AST output regardless of input format
  - Microsoft.Extensions.AI inspired patterns without the dependency

#### 2. StructuredResponse Models (`/src/Andy.Llm/Parsing/StructuredResponse.cs`)
- **StructuredLlmResponse**: Container for structured API responses
- **StructuredToolCall**: Represents a tool/function call with safe argument parsing
- **StructuredToolResult**: Represents tool execution results
- **StructuredResponseMetadata**: Response metadata including token usage
- **IStructuredResponseFactory**: Interface for provider-specific response creation

#### 3. Enhanced AST Nodes (`/src/Andy.Llm/Parsing/Ast/LlmResponseAst.cs`)
- **ToolCallNode**: Enhanced with `ParseError` property for argument parsing failures
- **ToolResultNode**: New node type for tool execution results
- **ResponseMetadata**: Enhanced with structured API metadata support

## Requirements

### Functional Requirements

1. **Dual Mode Parsing**
   - Must handle structured API responses (JSON with separate tool_calls)
   - Must handle text-based responses (embedded tool calls in text)
   - Must automatically detect response format

2. **Error Resilience**
   - Never fail completely - always return some AST
   - Capture parsing errors in AST nodes
   - Provide meaningful fallback behavior

3. **Provider Support**
   - OpenAI API format (tool_calls array)
   - Anthropic API format (function calls)
   - Cerebras/Qwen text format (embedded JSON)
   - Generic text with tool mentions

4. **Safe Argument Parsing**
   - Capture malformed JSON without crashing
   - Store both raw JSON and parsed arguments
   - Include parse errors in AST for debugging

### Non-Functional Requirements

1. **Performance**
   - Minimal overhead for format detection
   - Efficient fallback without re-parsing
   - Support for streaming responses

2. **Maintainability**
   - Clear separation between structured and text parsing
   - Extensible factory pattern for new providers
   - Comprehensive logging for debugging

3. **Compatibility**
   - Work with existing ILlmResponseParser interface
   - Maintain backward compatibility with text-only parsers
   - Support future structured API formats

## Implementation Status

### ✅ Completed
- HybridLlmParser core implementation
- StructuredResponse models
- AST enhancements (ToolResultNode, ParseError)
- Safe argument parsing utilities
- Basic structured response detection
- Fallback mechanisms

### 🚧 TODO
1. **Complete IStructuredResponseFactory implementations**
   ```csharp
   // In DefaultStructuredResponseFactory
   public StructuredLlmResponse CreateFromOpenAI(object openAIResponse)
   {
       // TODO: Implement OpenAI ChatCompletion parsing
       // Extract tool_calls array
       // Map to StructuredToolCall objects
   }

   public StructuredLlmResponse CreateFromAnthropic(object anthropicResponse)
   {
       // TODO: Implement Anthropic response parsing
       // Handle their function call format
   }
   ```

2. **Add streaming support**
   ```csharp
   public async Task<ResponseNode> ParseStreamingAsync(
       IAsyncEnumerable<string> chunks,
       ParserContext? context = null,
       CancellationToken cancellationToken = default)
   {
       // TODO: Implement incremental parsing for structured responses
       // Handle partial JSON accumulation
       // Detect format from initial chunks
   }
   ```

3. **Improve format detection heuristics**
   ```csharp
   private static bool IsStructuredResponseFormat(string input)
   {
       // TODO: Add more sophisticated detection
       // Check for provider-specific patterns
       // Handle edge cases and partial responses
   }
   ```

## Testing Requirements

### Unit Tests Needed

1. **HybridLlmParser Tests**
   ```csharp
   [Fact]
   public void Parse_OpenAIStructuredResponse_ExtractsToolCalls()
   {
       // Test with actual OpenAI response format
   }

   [Fact]
   public void Parse_AnthropicResponse_ExtractsToolCalls()
   {
       // Test with Anthropic function call format
   }

   [Fact]
   public void Parse_MalformedStructured_FallsBackToText()
   {
       // Verify fallback behavior
   }

   [Fact]
   public void Parse_TextWithEmbeddedTools_ExtractsCorrectly()
   {
       // Test Qwen-style embedded tool calls
   }
   ```

2. **StructuredArgumentParser Tests**
   ```csharp
   [Fact]
   public void SafeParseArguments_ValidJson_ReturnsArguments()
   {
       // Test successful parsing
   }

   [Fact]
   public void SafeParseArguments_InvalidJson_ReturnsError()
   {
       // Test error capture
   }
   ```

3. **Factory Pattern Tests**
   ```csharp
   [Fact]
   public void CreateFromOpenAI_ValidResponse_CreatesStructured()
   {
       // Test factory creation
   }
   ```

### Integration Tests Needed

1. **End-to-End Parsing**
   - Test with real API responses from different providers
   - Verify AST consistency across formats
   - Test error recovery scenarios

2. **Performance Tests**
   - Benchmark structured vs text parsing
   - Measure fallback overhead
   - Test with large responses

## Usage Examples

### Basic Usage
```csharp
// Create parser with text fallback
var textParser = new QwenParser(jsonRepair, logger);
var structuredFactory = new DefaultStructuredResponseFactory();
var hybridParser = new HybridLlmParser(textParser, structuredFactory, logger);

// Parse any response type
var response = "..."; // Could be structured JSON or text
var ast = hybridParser.Parse(response);

// AST is consistent regardless of input format
foreach (var node in ast.Children)
{
    if (node is ToolCallNode toolCall)
    {
        if (toolCall.ParseError != null)
        {
            // Handle argument parsing error
            logger.LogWarning("Tool call parse error: {Error}", 
                toolCall.ParseError.Message);
        }
        else
        {
            // Use parsed arguments
            var args = toolCall.Arguments;
        }
    }
}
```

### Provider-Specific Usage
```csharp
// OpenAI structured response
var openAiResponse = @"{
    ""choices"": [{
        ""message"": {
            ""content"": ""I'll help you with that"",
            ""tool_calls"": [{
                ""id"": ""call_123"",
                ""type"": ""function"",
                ""function"": {
                    ""name"": ""search"",
                    ""arguments"": ""{\""query\"": \""test\""}""
                }
            }]
        }
    }]
}";

var ast = hybridParser.Parse(openAiResponse);
// Automatically detects structured format and extracts tool calls

// Qwen text response with embedded tool
var qwenResponse = @"I'll search for that information.
{""tool_call"": {""name"": ""search"", ""arguments"": {""query"": ""test""}}}
The search has been completed.";

var ast2 = hybridParser.Parse(qwenResponse);
// Falls back to text parser, extracts embedded tool call
```

### Custom Factory Implementation
```csharp
public class CustomStructuredResponseFactory : IStructuredResponseFactory
{
    public StructuredLlmResponse CreateFromOpenAI(object openAIResponse)
    {
        var response = new StructuredLlmResponse();
        
        // Custom OpenAI parsing logic
        if (openAIResponse is ChatCompletion completion)
        {
            var message = completion.Choices[0].Message;
            response.TextContent = message.Content;
            
            foreach (var toolCall in message.ToolCalls ?? [])
            {
                response.ToolCalls.Add(new StructuredToolCall
                {
                    Id = toolCall.Id,
                    Name = toolCall.Function.Name,
                    ArgumentsJson = toolCall.Function.Arguments
                });
            }
        }
        
        return response;
    }
    
    // Implement other methods...
}
```

## Documentation Requirements

### API Documentation
Each public class and method needs XML documentation:
```csharp
/// <summary>
/// Hybrid LLM response parser that handles both structured API responses 
/// and text-based responses using existing parsers as fallback.
/// </summary>
/// <remarks>
/// This parser attempts to detect and parse structured responses first,
/// falling back to text parsing when needed. It provides a unified AST
/// output regardless of the input format.
/// </remarks>
public class HybridLlmParser : ILlmResponseParser
```

### README Addition
Add to andy-llm README.md:
```markdown
## Hybrid Response Parsing

The andy-llm package includes a hybrid parser that automatically handles both:
- **Structured API responses** from OpenAI, Anthropic, Azure
- **Text-based responses** from Qwen, Ollama, and other text models

### Quick Start
```csharp
var hybridParser = new HybridLlmParser(textParser, structuredFactory);
var ast = hybridParser.Parse(response); // Works with any format
```

### Features
- Automatic format detection
- Safe error handling with fallbacks
- Unified AST output
- Tool call extraction from any format
```

## Migration Guide

### For Existing Code
```csharp
// Old: Direct parser usage
var parser = new QwenParser(jsonRepair, logger);
var ast = parser.Parse(response);

// New: Hybrid parser with fallback
var textParser = new QwenParser(jsonRepair, logger);
var hybridParser = new HybridLlmParser(
    textParser, 
    new DefaultStructuredResponseFactory(), 
    logger);
var ast = hybridParser.Parse(response);
```

### For Tool Execution
```csharp
// Check for parse errors before execution
foreach (var node in ast.Children.OfType<ToolCallNode>())
{
    if (node.ParseError != null)
    {
        // Log error, skip execution, or attempt recovery
        continue;
    }
    
    // Safe to execute tool
    var result = await ExecuteTool(node.ToolName, node.Arguments);
    
    // Add result to AST
    ast.Children.Add(new ToolResultNode
    {
        CallId = node.CallId,
        ToolName = node.ToolName,
        Result = result,
        IsSuccess = true
    });
}
```

## Package Configuration

### Dependencies to Add
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.0" />
  <!-- Already added -->
</ItemGroup>
```

### Package Version Bump
Update in Directory.Build.props:
```xml
<PropertyGroup>
  <Version>2025.9.5-rc.1</Version> <!-- Bump from 2025.9.4-rc.10 -->
</PropertyGroup>
```

## Release Checklist

- [ ] Complete TODO implementations
- [ ] Add comprehensive unit tests
- [ ] Add integration tests with real provider responses
- [ ] Update API documentation
- [ ] Update README with usage examples
- [ ] Add migration guide to release notes
- [ ] Run performance benchmarks
- [ ] Test with andy-cli integration
- [ ] Bump package version
- [ ] Publish to NuGet

## Known Issues & Limitations

1. **Streaming Support**: Currently delegates to text parser for streaming
2. **Provider Detection**: Heuristics may need tuning for edge cases
3. **Partial Responses**: May not handle incomplete structured responses well
4. **Factory Implementations**: OpenAI and Anthropic factories are stubs

## Future Enhancements

1. **Provider Auto-Detection**: Detect provider from response patterns
2. **Response Caching**: Cache parsed structured responses
3. **Metrics Collection**: Track parser performance and fallback rates
4. **Schema Validation**: Validate tool arguments against schemas
5. **Response Transformation**: Convert between provider formats
6. **Parallel Parsing**: Try multiple parsers concurrently for speed

## Support & Contribution

For issues or contributions related to the hybrid parser:
1. Check existing issues in the andy-llm repository
2. Provide example responses that fail to parse correctly
3. Include provider information and response format
4. Consider contributing provider-specific factory implementations

---

*This document should be moved to the andy-llm repository before publishing the new package version.*