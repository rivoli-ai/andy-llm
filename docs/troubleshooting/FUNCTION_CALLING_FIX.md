# Function Calling Fix

## Issue
The FunctionCalling example was failing with error:
```
HTTP 400 (invalid_request_error: ) Parameter: messages.[2].role  
Invalid parameter: messages with role 'tool' must be a response to a preceding message with 'tool_calls'.
```

## Root Cause
When the assistant returns function calls, the conversation flow must be:
1. User message
2. Assistant message WITH function calls (tool_calls)
3. Tool response message(s)
4. Assistant final response

The example was missing step 2 - it wasn't adding the assistant's message with function calls to the conversation context before adding tool responses.

## Solution
Added `context.AddAssistantMessageWithToolCalls()` calls to properly track the assistant's function call requests:

```csharp
// When assistant wants to call functions
if (response.FunctionCalls != null && response.FunctionCalls.Any())
{
    // IMPORTANT: Add the assistant's message with function calls to context
    context.AddAssistantMessageWithToolCalls(response.Content, response.FunctionCalls);
    
    // Then execute functions and add tool responses
    foreach (var call in response.FunctionCalls)
    {
        // Execute function...
        context.AddToolResponse(call.Name, call.Id, result);
    }
    
    // Finally get the assistant's final response
    var finalResponse = await client.CompleteAsync(context.CreateRequest());
}
```

## Files Modified
- `examples/FunctionCalling/Program.cs` - Added proper assistant message tracking

## Testing
The function calling example should now work properly:
```bash
dotnet run --project examples/FunctionCalling
```

The conversation will flow correctly with the assistant making function calls and receiving tool responses.