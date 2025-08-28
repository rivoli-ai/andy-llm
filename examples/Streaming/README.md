# Streaming Example

This example demonstrates real-time streaming responses from LLM providers using the Andy.Llm library.

## What It Does

The streaming example showcases five different streaming scenarios:

1. **Basic Streaming**: Shows real-time token-by-token output as the LLM generates a response
2. **Streaming with Cancellation**: Demonstrates how to cancel a stream after a timeout
3. **Streaming with Progress**: Tracks character and chunk counts during streaming
4. **Streaming with Error Handling**: Shows how to handle errors and truncation in streams
5. **Streaming with Function Calls**: Demonstrates function calling in streaming mode (if supported by provider)

## Prerequisites

You need an OpenAI API key set as an environment variable:

```bash
export OPENAI_API_KEY=sk-your-api-key-here
```

## Running the Example

From the repository root:

```bash
dotnet run --project examples/Streaming
```

## Expected Output

When running successfully, you should see:

```
=== Streaming Examples ===

Starting streaming demonstrations with OpenAI...

=== Example 1: Basic Streaming ===
Streaming response:
[Real-time text appears here character by character as the LLM generates a poem about programming]
[Stream complete]

=== Example 2: Streaming with Cancellation ===
Streaming (will cancel after 2 seconds):
[Partial output before cancellation]
[Stream cancelled]

=== Example 3: Streaming with Progress ===
Streaming response with character count:
[Real-time text with progress tracking]
[Stream complete: 245 characters in 12 chunks]

=== Example 4: Streaming with Error Handling ===
Streaming with error handling:
[Text that may be truncated due to token limit]
[Output truncated due to token limit]

=== Example 5: Streaming with Function Calls ===
Note: Function calling in streaming mode may vary by provider
[May show function calls if supported]

Streaming examples completed!
```

## Key Features Demonstrated

- **Real-time Output**: Text appears character-by-character as it's generated
- **Cancellation Support**: Ability to stop long-running streams
- **Progress Tracking**: Monitor streaming progress with metrics
- **Error Handling**: Gracefully handle truncation and errors
- **Function Calling**: Stream function calls (provider-dependent)

## Troubleshooting

If you see no output:
1. Check that your OPENAI_API_KEY is set correctly
2. Ensure you have internet connectivity
3. Verify the API key has sufficient credits
4. Check the console for error messages

The example uses `Console.Write` for real-time character output and the logger for status messages, providing a clean streaming experience.