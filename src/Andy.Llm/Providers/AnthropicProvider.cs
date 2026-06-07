using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Andy.Llm.Configuration;
using Andy.Llm.Parsing;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Llm.Providers;

/// <summary>
/// Provider for the Anthropic Messages API (Claude family).
///
/// Implementation notes:
/// - Anthropic publishes no official .NET SDK, so this is a direct
///   <see cref="HttpClient"/> implementation against
///   <c>POST /v1/messages</c>. Same shape as <see cref="OllamaProvider"/>.
/// - The system prompt is a top-level field on the request body, not a
///   <c>role: "system"</c> message — the converter strips system messages
///   and surfaces them via <see cref="LlmRequest.SystemPrompt"/>.
/// - <c>max_tokens</c> is a required field on Anthropic's API; when
///   <see cref="LlmRequest.MaxTokens"/> is unset we fall back to
///   <see cref="DefaultMaxTokens"/> rather than failing with HTTP 400.
/// - Anthropic exposes no public <c>/models</c> endpoint, so
///   <see cref="ListModelsAsync"/> returns a hard-coded snapshot of the
///   currently-shipping Claude family. Bump this list when Anthropic
///   releases a new model rather than introducing a discovery call that
///   doesn't exist.
/// </summary>
public class AnthropicProvider : Andy.Model.Llm.ILlmProvider
{
    internal const string DefaultApiBase = "https://api.anthropic.com";
    internal const string DefaultAnthropicVersion = "2023-06-01";
    internal const int DefaultMaxTokens = 4096;

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicProvider> _logger;
    private readonly string _defaultModel;
    private readonly string _configName;

    /// <inheritdoc />
    public string Name => _configName;

    /// <summary>
    /// Initializes a new instance of the Anthropic provider.
    /// </summary>
    public AnthropicProvider(
        ProviderConfig config,
        string configName,
        ILogger<AnthropicProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var providerConfig = config ?? throw new ArgumentNullException(nameof(config));
        _configName = configName ?? "anthropic";

        if (string.IsNullOrEmpty(providerConfig.ApiKey))
        {
            throw new InvalidOperationException($"Anthropic API key not configured for '{configName}'");
        }
        if (string.IsNullOrEmpty(providerConfig.Model))
        {
            throw new InvalidOperationException($"Anthropic model not configured for '{configName}'");
        }

        _defaultModel = providerConfig.Model;

        var baseUrl = (providerConfig.ApiBase ?? DefaultApiBase).TrimEnd('/') + "/";

        _httpClient = httpClientFactory?.CreateClient("Anthropic") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);

        _httpClient.DefaultRequestHeaders.Remove("x-api-key");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", providerConfig.ApiKey);
        _httpClient.DefaultRequestHeaders.Remove("anthropic-version");
        _httpClient.DefaultRequestHeaders.Add(
            "anthropic-version", providerConfig.ApiVersion ?? DefaultAnthropicVersion);

        _logger.LogInformation(
            "Anthropic provider initialized - Config: {ConfigName}, Model: {Model}",
            _configName, _defaultModel);
    }

    /// <summary>
    /// Backward-compatibility constructor used by DI registration in
    /// <see cref="Extensions.ServiceCollectionExtensions"/>. Resolves the
    /// first Anthropic-flavoured <see cref="ProviderConfig"/> from
    /// <see cref="LlmOptions"/> and delegates to the primary ctor.
    /// Production code paths use <c>ILlmProviderFactory.CreateProvider</c>
    /// instead, which constructs providers directly per alias.
    /// </summary>
    public AnthropicProvider(
        IOptions<LlmOptions> options,
        ILogger<AnthropicProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
        : this(ResolveConfig(options), ResolveConfigName(options), logger, httpClientFactory)
    {
    }

    private static ProviderConfig ResolveConfig(IOptions<LlmOptions> options)
    {
        var match = options.Value.Providers
            .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key, "anthropic", StringComparison.OrdinalIgnoreCase));
        if (match.Value != null)
        {
            return match.Value;
        }

        // Fall back to env vars so the DI-resolved provider works in
        // tests that haven't populated Providers explicitly.
        return new ProviderConfig
        {
            Provider = "anthropic",
            ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
            ApiBase = Environment.GetEnvironmentVariable("ANTHROPIC_API_BASE") ?? DefaultApiBase,
            Model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL"),
            ApiVersion = Environment.GetEnvironmentVariable("ANTHROPIC_API_VERSION")
        };
    }

    private static string ResolveConfigName(IOptions<LlmOptions> options)
    {
        var match = options.Value.Providers
            .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key, "anthropic", StringComparison.OrdinalIgnoreCase));
        return match.Key ?? "anthropic";
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Anthropic exposes no health endpoint, so issue the smallest
        // possible completion (1 output token). A 200 means the API key
        // is good and the model accepts the request shape.
        try
        {
            // Build a 1-token ping. LlmRequest's per-call overrides are
            // read-only after construction, so wire the model + cap via
            // the ctor's init slots and rely on BuildRequest's
            // LlmRequest.Model-or-default fallback for cases where the
            // builder constructor doesn't surface them.
            var ping = new LlmRequest
            {
                Messages = new List<Message>
                {
                    new Message { Role = Role.User, Content = "ping" }
                },
                Config = new LlmClientConfig
                {
                    Model = _defaultModel,
                    MaxTokens = 1
                }
            };
            await CompleteAsync(ping, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Anthropic availability check failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildRequest(request, stream: false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("v1/messages", body, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Anthropic request failed");
            throw new InvalidOperationException($"Anthropic request failed: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Anthropic returned {Status}: {Body}", (int)response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Anthropic request returned HTTP {(int)response.StatusCode}: {Truncate(errorBody, 500)}");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<AnthropicMessagesResponse>(payload)
                     ?? throw new InvalidOperationException("Anthropic returned null/unparseable response body");

        return ConvertResponse(parsed, request.Model ?? _defaultModel);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequest(request, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;
        string? errorMessage = null;

        try
        {
            response = await _httpClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Anthropic streaming returned {Status}: {Body}",
                    (int)response.StatusCode, errBody);
                errorMessage = $"Anthropic streaming returned HTTP {(int)response.StatusCode}: {Truncate(errBody, 500)}";
            }
            else
            {
                stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                reader = new StreamReader(stream);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Anthropic streaming request failed");
            errorMessage = $"Anthropic streaming request failed: {ex.Message}";
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Anthropic streaming cancelled before headers");
        }

        if (errorMessage != null)
        {
            yield return new LlmStreamResponse { Error = errorMessage };
            yield break;
        }

        if (reader == null)
        {
            yield break;
        }

        // Per-content-block accumulator for tool_use input deltas (Anthropic
        // emits the tool argument JSON in `input_json_delta` chunks within a
        // content_block_delta event). Indexed by `content_block.index`.
        var toolBlocks = new Dictionary<int, AccumulatingToolBlock>();
        string? finalStopReason = null;
        int? finalInputTokens = null;
        int? finalOutputTokens = null;

        try
        {
            string? eventName = null;
            string? dataLine = null;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Anthropic streaming cancelled during read");
                    break;
                }

                // Blank line = end of an SSE event. Process whatever we have.
                if (string.IsNullOrEmpty(line))
                {
                    if (eventName != null && dataLine != null)
                    {
                        AnthropicStreamEvent? parsed = TryParseEvent(dataLine);
                        if (parsed != null)
                        {
                            foreach (var chunk in HandleEvent(eventName, parsed, toolBlocks,
                                         ref finalStopReason, ref finalInputTokens, ref finalOutputTokens))
                            {
                                yield return chunk;
                            }
                        }
                    }
                    eventName = null;
                    dataLine = null;
                    continue;
                }

                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventName = line.Substring("event: ".Length).Trim();
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    dataLine = line.Substring("data: ".Length);
                }
                // Other SSE field types (id:, retry:) are ignored — Anthropic
                // doesn't use them today and the spec says unknown lines are
                // safe to skip.
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }

        // Final synthetic completion frame with usage + finish reason. Mirrors
        // ChatCompletionsStrategy's behaviour so downstream code doesn't have
        // to special-case Anthropic.
        var accumulatedToolCalls = toolBlocks.Count > 0
            ? toolBlocks
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new ToolCall
                {
                    Id = kvp.Value.Id ?? string.Empty,
                    Name = kvp.Value.Name ?? string.Empty,
                    ArgumentsJson = ToolArgumentJsonRepair.Normalize(kvp.Value.InputJson)
                })
                .ToList()
            : null;

        yield return new LlmStreamResponse
        {
            Delta = new Message
            {
                Role = Role.Assistant,
                ToolCalls = accumulatedToolCalls
            },
            FinishReason = finalStopReason,
            IsComplete = true,
            Usage = (finalInputTokens.HasValue || finalOutputTokens.HasValue) ? new LlmUsage
            {
                PromptTokens = finalInputTokens ?? 0,
                CompletionTokens = finalOutputTokens ?? 0,
                TotalTokens = (finalInputTokens ?? 0) + (finalOutputTokens ?? 0)
            } : null
        };
    }

    /// <inheritdoc />
    public Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        // Anthropic publishes no models endpoint. Hard-coded snapshot of
        // the family currently in production. Update when a new model
        // ships rather than calling a non-existent discovery API.
        // Sources: https://docs.anthropic.com/en/docs/about-claude/models
        var models = new[]
        {
            BuildModelInfo("claude-opus-4-5-20251101", "Claude Opus 4.5", "Most capable Claude model", 200_000),
            BuildModelInfo("claude-sonnet-4-5-20250929", "Claude Sonnet 4.5", "Balanced performance and cost", 200_000),
            BuildModelInfo("claude-haiku-4-5-20251001", "Claude Haiku 4.5", "Fastest, smallest Claude 4 model", 200_000),
            BuildModelInfo("claude-3-7-sonnet-20250219", "Claude 3.7 Sonnet", "Extended thinking-capable Sonnet", 200_000),
            BuildModelInfo("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet", "Previous-generation Sonnet", 200_000),
            BuildModelInfo("claude-3-5-haiku-20241022", "Claude 3.5 Haiku", "Previous-generation Haiku", 200_000),
        };

        return Task.FromResult<IEnumerable<ModelInfo>>(models);
    }

    #region Request building

    internal static AnthropicMessagesRequest BuildRequest(LlmRequest request, bool stream)
    {
        var anthropicMessages = new List<AnthropicMessage>();
        string? systemPrompt = request.SystemPrompt;

        foreach (var msg in request.Messages)
        {
            // System messages: collapse into the top-level `system` field
            // rather than passing through, since Anthropic doesn't accept
            // role: "system" inside `messages`.
            if (msg.Role == Role.System)
            {
                var sysText = ExtractText(msg);
                if (!string.IsNullOrEmpty(sysText))
                {
                    systemPrompt = string.IsNullOrEmpty(systemPrompt)
                        ? sysText
                        : systemPrompt + "\n\n" + sysText;
                }
                continue;
            }

            var blocks = BuildContentBlocks(msg);
            if (blocks.Count == 0)
            {
                continue;
            }

            // If the caller marked this message as a cache breakpoint, attach
            // cache_control to its last content block. Anthropic caches the
            // prefix up to and including the marked block.
            if (msg.CacheControl is { } cc)
            {
                blocks[^1].CacheControl = new AnthropicCacheControl { Type = cc.Type };
            }

            anthropicMessages.Add(new AnthropicMessage
            {
                Role = msg.Role == Role.Assistant ? "assistant" : "user",
                Content = blocks
            });
        }

        // The system prompt is normally serialized as a bare string. When the
        // caller opts into prompt caching for it, we must render it as a single
        // text content block so the cache_control breakpoint has somewhere to
        // live (Anthropic rejects cache_control on a string system field).
        object? systemField = null;
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            if (request.CacheSystemPrompt)
            {
                systemField = new List<AnthropicContentBlock>
                {
                    new()
                    {
                        Type = "text",
                        Text = systemPrompt,
                        CacheControl = new AnthropicCacheControl { Type = "ephemeral" }
                    }
                };
            }
            else
            {
                systemField = systemPrompt;
            }
        }

        var body = new AnthropicMessagesRequest
        {
            Model = request.Model ?? throw new InvalidOperationException("LlmRequest.Model is required for Anthropic"),
            MaxTokens = request.MaxTokens > 0 ? request.MaxTokens : DefaultMaxTokens,
            System = systemField,
            Messages = anthropicMessages,
            Stream = stream
        };

        if (request.Temperature.HasValue)
        {
            body.Temperature = (double)request.Temperature.Value;
        }

        if (request.Tools?.Any() == true)
        {
            body.Tools = request.Tools.Select(t => new AnthropicTool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = ConvertToolSchema(t.Parameters)
            }).ToList();
        }

        return body;
    }

    private static JsonElement ConvertToolSchema(object? parameters)
    {
        // ToolDeclaration.Parameters is `object?` (typically a JSON-shaped
        // POCO or dictionary). Anthropic wants this serialised as the
        // `input_schema` JSON object. If the caller passed null, fall back
        // to an empty object schema so the API doesn't 400.
        if (parameters is null)
        {
            using var doc = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
            return doc.RootElement.Clone();
        }

        // If it's already a JsonElement, pass it through.
        if (parameters is JsonElement elem)
        {
            return elem.Clone();
        }

        var json = JsonSerializer.Serialize(parameters);
        using var doc2 = JsonDocument.Parse(json);
        return doc2.RootElement.Clone();
    }

    private static List<AnthropicContentBlock> BuildContentBlocks(Message message)
    {
        var blocks = new List<AnthropicContentBlock>();

        // Tool results (role=Tool messages) become `tool_result` content
        // blocks. Anthropic carries these on a user-role message.
        if (message.Role == Role.Tool)
        {
            // Two shapes are possible: a single ToolCallId/Content pair on
            // the Message itself, or a Parts list of ToolResponsePart. Cover
            // both.
            if (!string.IsNullOrEmpty(message.ToolCallId))
            {
                blocks.Add(new AnthropicContentBlock
                {
                    Type = "tool_result",
                    ToolUseId = message.ToolCallId,
                    Content = message.Content ?? string.Empty
                });
            }

            foreach (var resp in message.Parts.OfType<ToolResponsePart>())
            {
                blocks.Add(new AnthropicContentBlock
                {
                    Type = "tool_result",
                    ToolUseId = resp.ToolResult.CallId,
                    Content = resp.ToolResult.ResultJson ?? string.Empty
                });
            }

            return blocks;
        }

        // Text parts (or the bare Content field) become text blocks.
        var textParts = message.Parts.OfType<TextPart>().ToList();
        if (textParts.Count > 0)
        {
            foreach (var t in textParts)
            {
                if (!string.IsNullOrEmpty(t.Text))
                {
                    blocks.Add(new AnthropicContentBlock { Type = "text", Text = t.Text });
                }
            }
        }
        else if (!string.IsNullOrEmpty(message.Content))
        {
            blocks.Add(new AnthropicContentBlock { Type = "text", Text = message.Content });
        }

        // Assistant tool calls become tool_use blocks.
        if (message.Role == Role.Assistant)
        {
            foreach (var tc in message.Parts.OfType<ToolCallPart>())
            {
                blocks.Add(new AnthropicContentBlock
                {
                    Type = "tool_use",
                    Id = tc.ToolCall.Id,
                    Name = tc.ToolCall.Name,
                    Input = ParseArgs(tc.ToolCall.ArgumentsJson)
                });
            }

            // Also handle the convenience Message.ToolCalls list (some
            // call sites populate ToolCalls directly rather than via Parts).
            if (message.ToolCalls is { Count: > 0 } toolCalls)
            {
                foreach (var tc in toolCalls)
                {
                    blocks.Add(new AnthropicContentBlock
                    {
                        Type = "tool_use",
                        Id = tc.Id,
                        Name = tc.Name,
                        Input = ParseArgs(tc.ArgumentsJson)
                    });
                }
            }
        }

        return blocks;
    }

    private static JsonElement ParseArgs(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Treat malformed tool arguments as an empty object rather
            // than 400-ing the whole completion. ChatCompletionsStrategy
            // does the same in practice.
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }

    private static string ExtractText(Message message)
    {
        var parts = message.Parts.OfType<TextPart>().Select(p => p.Text).ToList();
        if (parts.Count > 0)
        {
            return string.Join("\n", parts);
        }
        return message.Content ?? string.Empty;
    }

    #endregion

    #region Response conversion

    private LlmResponse ConvertResponse(AnthropicMessagesResponse response, string model)
    {
        var textBuilder = new StringBuilder();
        var toolCalls = new List<ToolCall>();

        if (response.Content != null)
        {
            foreach (var block in response.Content)
            {
                switch (block.Type)
                {
                    case "text":
                        if (!string.IsNullOrEmpty(block.Text))
                        {
                            textBuilder.Append(block.Text);
                        }
                        break;
                    case "tool_use":
                        toolCalls.Add(new ToolCall
                        {
                            Id = block.Id ?? string.Empty,
                            Name = block.Name ?? string.Empty,
                            ArgumentsJson = block.Input.HasValue && block.Input.Value.ValueKind != JsonValueKind.Undefined
                                ? ToolArgumentJsonRepair.Normalize(block.Input.Value.GetRawText())
                                : "{}"
                        });
                        break;
                }
            }
        }

        return new LlmResponse
        {
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = textBuilder.ToString(),
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null
            },
            Model = response.Model ?? model,
            FinishReason = response.StopReason,
            Usage = response.Usage != null ? new LlmUsage
            {
                PromptTokens = response.Usage.InputTokens,
                CompletionTokens = response.Usage.OutputTokens,
                TotalTokens = response.Usage.InputTokens + response.Usage.OutputTokens
            } : null
        };
    }

    #endregion

    #region Stream parsing

    private static AnthropicStreamEvent? TryParseEvent(string dataLine)
    {
        try
        {
            return JsonSerializer.Deserialize<AnthropicStreamEvent>(dataLine);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IEnumerable<LlmStreamResponse> HandleEvent(
        string eventName,
        AnthropicStreamEvent ev,
        Dictionary<int, AccumulatingToolBlock> toolBlocks,
        ref string? finalStopReason,
        ref int? finalInputTokens,
        ref int? finalOutputTokens)
    {
        switch (eventName)
        {
            case "message_start":
                if (ev.Message?.Usage != null)
                {
                    finalInputTokens = ev.Message.Usage.InputTokens;
                    finalOutputTokens = ev.Message.Usage.OutputTokens;
                }
                return Enumerable.Empty<LlmStreamResponse>();

            case "content_block_start":
                if (ev.ContentBlock is { Type: "tool_use" } block && ev.Index.HasValue)
                {
                    toolBlocks[ev.Index.Value] = new AccumulatingToolBlock
                    {
                        Id = block.Id,
                        Name = block.Name
                    };
                }
                return Enumerable.Empty<LlmStreamResponse>();

            case "content_block_delta":
                if (ev.Delta is null) return Enumerable.Empty<LlmStreamResponse>();

                if (ev.Delta.Type == "text_delta" && !string.IsNullOrEmpty(ev.Delta.Text))
                {
                    return new[]
                    {
                        new LlmStreamResponse
                        {
                            Delta = new Message
                            {
                                Role = Role.Assistant,
                                Content = ev.Delta.Text
                            },
                            IsComplete = false
                        }
                    };
                }

                if (ev.Delta.Type == "input_json_delta"
                    && ev.Index.HasValue
                    && toolBlocks.TryGetValue(ev.Index.Value, out var accumulator)
                    && !string.IsNullOrEmpty(ev.Delta.PartialJson))
                {
                    accumulator.InputJson += ev.Delta.PartialJson;
                }
                return Enumerable.Empty<LlmStreamResponse>();

            case "content_block_stop":
                return Enumerable.Empty<LlmStreamResponse>();

            case "message_delta":
                if (ev.Delta?.StopReason is { } stopReason)
                {
                    finalStopReason = stopReason;
                }
                if (ev.Usage != null)
                {
                    // message_delta carries the final output_tokens count.
                    finalOutputTokens = ev.Usage.OutputTokens;
                }
                return Enumerable.Empty<LlmStreamResponse>();

            case "message_stop":
                return Enumerable.Empty<LlmStreamResponse>();

            default:
                return Enumerable.Empty<LlmStreamResponse>();
        }
    }

    #endregion

    #region Model metadata

    private static ModelInfo BuildModelInfo(string id, string name, string description, int contextWindow)
    {
        return new ModelInfo
        {
            Id = id,
            Name = name,
            Provider = "anthropic",
            Description = description,
            Family = "Claude",
            MaxTokens = contextWindow,
            SupportsFunctions = true,
            SupportsVision = true,
        };
    }

    #endregion

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max));

    private sealed class AccumulatingToolBlock
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string InputJson { get; set; } = string.Empty;
    }
}

#region Wire types

internal class AnthropicMessagesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    // Anthropic accepts the system prompt either as a plain string or as an
    // array of content blocks. We need the block form whenever a cache
    // breakpoint must be attached (cache_control lives on a block, not on a
    // bare string), so this is typed as object? and populated with either a
    // string or a List<AnthropicContentBlock> by BuildRequest.
    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? System { get; set; }

    [JsonPropertyName("messages")]
    public required List<AnthropicMessage> Messages { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AnthropicTool>? Tools { get; set; }
}

internal class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required List<AnthropicContentBlock> Content { get; set; }
}

internal class AnthropicContentBlock
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }

    // Prompt-caching breakpoint. When set, Anthropic caches the prefix up to
    // and including this block. Serialized as {"type":"ephemeral"}.
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnthropicCacheControl? CacheControl { get; set; }
}

internal class AnthropicCacheControl
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }
}

internal class AnthropicTool
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; set; }
}

internal class AnthropicMessagesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public List<AnthropicContentBlock>? Content { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}

internal class AnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

internal class AnthropicStreamEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("message")]
    public AnthropicMessagesResponse? Message { get; set; }

    [JsonPropertyName("content_block")]
    public AnthropicContentBlock? ContentBlock { get; set; }

    [JsonPropertyName("delta")]
    public AnthropicStreamDelta? Delta { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}

internal class AnthropicStreamDelta
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("partial_json")]
    public string? PartialJson { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}

#endregion
