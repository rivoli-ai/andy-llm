using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Llm.Parsing;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;

namespace Andy.Llm.Providers;

/// <summary>
/// Strategy implementation for the OpenAI Responses API (/v1/responses).
///
/// The Responses API is required for Codex models (codex-mini-latest, gpt-5-codex,
/// gpt-5.1-codex-*, gpt-5.2-codex) and supports features not available in Chat Completions:
/// - Built-in tools (code_interpreter, image_generation, MCP)
/// - Reasoning persistence across turns (via previous_response_id or encrypted reasoning)
/// - Background execution for long-running tasks
///
/// Key differences from Chat Completions:
/// - Endpoint: POST /v1/responses (not /v1/chat/completions)
/// - Input: "input" array (not "messages"), system prompt via "instructions" (not system message)
/// - Output: "output" array with typed items (not choices[0].message)
/// - Tool calls: "function_call" type items with "call_id" (not tool_calls with "id")
/// - Tool results: "function_call_output" type items (not tool role messages)
/// - Token usage: input_tokens/output_tokens (not prompt_tokens/completion_tokens)
/// </summary>
internal class ResponsesApiStrategy : IOpenAIApiStrategy
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <inheritdoc />
    public string ApiType => "responses";

    /// <summary>
    /// Creates a new Responses API strategy.
    /// </summary>
    /// <param name="httpClient">HTTP client configured with base URL and auth headers.</param>
    /// <param name="model">The model to use (e.g., "codex-mini-latest").</param>
    /// <param name="logger">Logger instance.</param>
    public ResponsesApiStrategy(HttpClient httpClient, string model, ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = BuildRequestBody(request, stream: false);
            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            _logger.LogDebug("Responses API request: {Request}", json);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);

            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Responses API error {StatusCode}: {Body}", httpResponse.StatusCode, responseBody);
                throw new InvalidOperationException(
                    $"OpenAI Responses API request failed (status {(int)httpResponse.StatusCode} {httpResponse.StatusCode}): {responseBody}");
            }

            _logger.LogDebug("Responses API response: {Response}", responseBody);

            return ParseResponse(responseBody);
        }
        catch (InvalidOperationException)
        {
            // Status-code path above already shaped this; re-throw verbatim.
            throw;
        }
        catch (Exception ex)
        {
            // See CerebrasProvider for rationale. Surface failures rather
            // than synthesising a fake assistant response.
            _logger.LogError(ex, "Error during OpenAI Responses API call: {Message}", ex.Message);
            throw new InvalidOperationException(
                $"OpenAI Responses API request failed: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestBody = BuildRequestBody(request, stream: true);
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);

        _logger.LogDebug("Responses API streaming request: {Request}", json);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Responses API streaming error {StatusCode}: {Body}", httpResponse.StatusCode, errorBody);

            yield return new LlmStreamResponse
            {
                Delta = new Message
                {
                    Role = Role.Assistant,
                    Content = $"OpenAI Responses API Error ({httpResponse.StatusCode}): {errorBody}"
                },
                IsComplete = true,
                FinishReason = "error"
            };
            yield break;
        }

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var accumulatedText = new StringBuilder();
        var toolCalls = new Dictionary<string, AccumulatedFunctionCall>();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
                continue;

            // SSE format: "event: <type>" followed by "data: <json>"
            if (line.StartsWith("event: "))
            {
                var eventType = line["event: ".Length..];
                var dataLine = await reader.ReadLineAsync(cancellationToken);

                if (dataLine == null || !dataLine.StartsWith("data: "))
                    continue;

                var data = dataLine["data: ".Length..];

                foreach (var chunk in ProcessStreamEvent(eventType, data, toolCalls))
                {
                    yield return chunk;
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var requestBody = new ResponsesRequest
            {
                Model = _model,
                Input = "test",
                MaxOutputTokens = 1
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Responses API availability check failed");
            return false;
        }
    }

    #region Request Building

    private ResponsesRequest BuildRequestBody(LlmRequest request, bool stream)
    {
        var body = new ResponsesRequest
        {
            Model = _model,
            Stream = stream ? true : null,
            Instructions = request.SystemPrompt,
            Temperature = (double?)request.Temperature,
            MaxOutputTokens = request.MaxTokens
        };

        // Convert messages to Responses API input format
        var input = new List<object>();

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case Role.User:
                    input.Add(new ResponsesInputMessage
                    {
                        Role = "user",
                        Content = message.Content
                    });
                    break;

                case Role.Assistant:
                    if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                    {
                        // Assistant message with tool calls — emit function_call items
                        if (!string.IsNullOrEmpty(message.Content))
                        {
                            input.Add(new ResponsesInputMessage
                            {
                                Role = "assistant",
                                Content = message.Content
                            });
                        }

                        foreach (var tc in message.ToolCalls)
                        {
                            input.Add(new ResponsesFunctionCall
                            {
                                CallId = tc.Id,
                                Name = tc.Name,
                                Arguments = tc.ArgumentsJson
                            });
                        }
                    }
                    else
                    {
                        input.Add(new ResponsesInputMessage
                        {
                            Role = "assistant",
                            Content = message.Content
                        });
                    }
                    break;

                case Role.Tool:
                    input.Add(new ResponsesFunctionCallOutput
                    {
                        CallId = message.ToolCallId ?? "",
                        Output = message.Content ?? ""
                    });
                    break;

                case Role.System:
                    // System messages go into instructions, but if there are multiple,
                    // append to the input as user context
                    if (string.IsNullOrEmpty(body.Instructions))
                    {
                        body.Instructions = message.Content;
                    }
                    break;
            }
        }

        body.Input = input.Count > 0 ? input : null;

        // Convert tools
        if (request.Tools?.Any() == true)
        {
            body.Tools = request.Tools.Select(t => new ResponsesToolDefinition
            {
                Type = "function",
                Name = t.Name,
                Description = t.Description,
                Parameters = t.Parameters
            }).ToList();
        }

        return body;
    }

    #endregion

    #region Response Parsing

    private LlmResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var content = "";
        var toolCalls = new List<ToolCall>();
        var finishReason = "stop";

        // Extract output_text for simple text responses
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            content = outputText.GetString() ?? "";
        }

        // Parse output items for tool calls and structured content
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;

                switch (type)
                {
                    case "message":
                        // Extract text from message content
                        if (item.TryGetProperty("content", out var msgContent) && msgContent.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var contentItem in msgContent.EnumerateArray())
                            {
                                var contentType = contentItem.TryGetProperty("type", out var ct) ? ct.GetString() : null;
                                if (contentType == "output_text" && contentItem.TryGetProperty("text", out var textProp))
                                {
                                    content = textProp.GetString() ?? "";
                                }
                            }
                        }
                        break;

                    case "function_call":
                        var callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() : null;
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var arguments = item.TryGetProperty("arguments", out var args) ? args.GetString() : "{}";

                        if (!string.IsNullOrEmpty(name))
                        {
                            toolCalls.Add(new ToolCall
                            {
                                Id = callId ?? $"call_{Guid.NewGuid():N}"[..12],
                                Name = name,
                                ArgumentsJson = ToolArgumentJsonRepair.Normalize(arguments)
                            });
                        }
                        break;
                }
            }
        }

        // If we have tool calls but no text content, that's normal
        if (toolCalls.Count > 0)
        {
            finishReason = "tool_calls";
        }

        // Extract status
        if (root.TryGetProperty("status", out var statusProp))
        {
            var status = statusProp.GetString();
            if (status == "failed")
                finishReason = "error";
        }

        // Extract usage
        LlmUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageProp))
        {
            var inputTokens = usageProp.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
            var outputTokens = usageProp.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            var totalTokens = usageProp.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : inputTokens + outputTokens;

            usage = new LlmUsage
            {
                PromptTokens = inputTokens,
                CompletionTokens = outputTokens,
                TotalTokens = totalTokens
            };
        }

        // Extract model
        var model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() : _model;

        _logger.LogInformation(
            "Responses API result - Content length: {ContentLen}, ToolCalls: {ToolCount}, FinishReason: {Reason}",
            content.Length, toolCalls.Count, finishReason);

        return new LlmResponse
        {
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = content,
                ToolCalls = toolCalls
            },
            FinishReason = finishReason,
            Usage = usage,
            Model = model
        };
    }

    private IEnumerable<LlmStreamResponse> ProcessStreamEvent(
        string eventType,
        string data,
        Dictionary<string, AccumulatedFunctionCall> toolCalls)
    {
        switch (eventType)
        {
            case "response.output_text.delta":
                // Text content delta
                using (var doc = JsonDocument.Parse(data))
                {
                    if (doc.RootElement.TryGetProperty("delta", out var delta))
                    {
                        var text = delta.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new LlmStreamResponse
                            {
                                Delta = new Message { Role = Role.Assistant, Content = text },
                                IsComplete = false
                            };
                        }
                    }
                }
                break;

            case "response.function_call_arguments.delta":
                // Partial function call arguments
                using (var doc = JsonDocument.Parse(data))
                {
                    var callId = doc.RootElement.TryGetProperty("call_id", out var cid) ? cid.GetString() : null;
                    var argDelta = doc.RootElement.TryGetProperty("delta", out var d) ? d.GetString() : null;

                    if (callId != null)
                    {
                        if (!toolCalls.TryGetValue(callId, out var acc))
                        {
                            acc = new AccumulatedFunctionCall { CallId = callId };
                            toolCalls[callId] = acc;
                        }

                        if (argDelta != null)
                            acc.Arguments += argDelta;
                    }
                }
                break;

            case "response.function_call_arguments.done":
                // Function call complete
                using (var doc = JsonDocument.Parse(data))
                {
                    var callId = doc.RootElement.TryGetProperty("call_id", out var cid) ? cid.GetString() : null;
                    var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var arguments = doc.RootElement.TryGetProperty("arguments", out var args) ? args.GetString() : null;

                    if (callId != null && !string.IsNullOrEmpty(name))
                    {
                        // Use accumulated arguments or the final arguments field
                        var finalArgs = arguments;
                        if (toolCalls.TryGetValue(callId, out var acc))
                        {
                            finalArgs ??= acc.Arguments;
                            toolCalls.Remove(callId);
                        }

                        yield return new LlmStreamResponse
                        {
                            Delta = new Message
                            {
                                Role = Role.Assistant,
                                ToolCalls = new List<ToolCall>
                                {
                                    new ToolCall
                                    {
                                        Id = callId,
                                        Name = name,
                                        ArgumentsJson = ToolArgumentJsonRepair.Normalize(finalArgs)
                                    }
                                }
                            },
                            IsComplete = false
                        };
                    }
                }
                break;

            case "response.completed":
                // Final event with usage
                LlmUsage? usage = null;
                using (var doc = JsonDocument.Parse(data))
                {
                    if (doc.RootElement.TryGetProperty("usage", out var usageProp))
                    {
                        var inputTokens = usageProp.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                        var outputTokens = usageProp.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;

                        usage = new LlmUsage
                        {
                            PromptTokens = inputTokens,
                            CompletionTokens = outputTokens,
                            TotalTokens = inputTokens + outputTokens
                        };
                    }
                }

                yield return new LlmStreamResponse
                {
                    IsComplete = true,
                    FinishReason = "stop",
                    Usage = usage
                };
                break;
        }
    }

    #endregion

    #region Request/Response Models

    /// <summary>
    /// OpenAI Responses API request body.
    /// The "input" field can be a simple string or an array of typed items.
    /// </summary>
    internal class ResponsesRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        /// <summary>
        /// Input can be a string (simple prompt) or a list of message/function_call/function_call_output items.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Input { get; set; }

        [JsonPropertyName("instructions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Instructions { get; set; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ResponsesToolDefinition>? Tools { get; set; }

        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature { get; set; }

        [JsonPropertyName("max_output_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxOutputTokens { get; set; }

        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Stream { get; set; }

        [JsonPropertyName("store")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Store { get; set; }
    }

    internal class ResponsesInputMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "message";

        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    internal class ResponsesFunctionCall
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function_call";

        [JsonPropertyName("call_id")]
        public string? CallId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
    }

    internal class ResponsesFunctionCallOutput
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function_call_output";

        [JsonPropertyName("call_id")]
        public string CallId { get; set; } = "";

        [JsonPropertyName("output")]
        public string Output { get; set; } = "";
    }

    internal class ResponsesToolDefinition
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("parameters")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Parameters { get; set; }
    }

    private class AccumulatedFunctionCall
    {
        public string CallId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
    }

    #endregion
}
