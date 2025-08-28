# Logging and Error Handling Improvements

## Summary
All examples in the Andy.Llm library have been updated to use consistent logging configuration and proper error handling.

## Changes Made

### 1. Consistent Logging Configuration
All examples now use the same logging configuration:
```csharp
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
```

This configuration provides:
- Clean, single-line output without timestamps
- No verbose prefixes like "info: Program[0]"
- Filtered HTTP client and provider debug logs
- Clear, readable console output

### 2. Logger Usage
- All `Console.WriteLine` calls have been replaced with appropriate logger methods:
  - `logger.LogInformation()` for normal output
  - `logger.LogError()` for errors
  - `logger.LogWarning()` for warnings
  - `logger.LogDebug()` for debug information

### 3. Proper Error Handling
All examples now include proper try-catch blocks with appropriate error logging:
```csharp
try
{
    // Example code
}
catch (Exception ex)
{
    logger.LogError(ex, "An error occurred during {Operation}", operationName);
}
```

### 4. Provider-Specific Improvements

#### Ollama Provider
- Automatic model detection from available models
- Better error messages for missing models with instructions
- Improved 404 error handling with specific guidance

#### Azure OpenAI Provider
- Clean logging output for configuration and availability checks
- Proper error handling for missing configuration

### 5. Examples Updated
- ✅ **SimpleCompletion**: Updated with logger and error handling
- ✅ **ConversationChat**: Interactive mode with proper error handling per request
- ✅ **FunctionCalling**: Function call logging and error handling
- ✅ **Streaming**: Comprehensive streaming error handling and progress tracking
- ✅ **MultiProvider**: Provider comparison with proper fallback and error handling
- ✅ **Telemetry**: Metrics logging with clean output
- ✅ **AzureOpenAI**: Enterprise scenarios with proper logging
- ✅ **Ollama**: Local model support with automatic detection

### 6. Key Benefits
- **Consistent Experience**: All examples follow the same logging pattern
- **Clean Output**: No more verbose prefixes cluttering the console
- **Better Debugging**: Proper error messages and stack traces when needed
- **Production Ready**: Examples demonstrate best practices for error handling
- **User Friendly**: Clear, actionable error messages with guidance

## Usage
The examples now provide a much cleaner output. For example:

**Before:**
```
info: Program[0]
      Ollama configuration:
info: Program[0]
        API Base: http://localhost:11434
info: System.Net.Http.HttpClient.Ollama.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:11434/api/tags
```

**After:**
```
Ollama configuration:
  API Base: http://localhost:11434
✓ Ollama is available!

Available Ollama models:
  - gpt-oss:20b
  - phi4:latest
```

## Note on Streaming Output
For streaming scenarios where real-time character-by-character output is needed (like in ConversationChat and Streaming examples), `Console.Write` is still used for the actual streamed content, while logging is used for all status messages and errors.