using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Andy.Llm.Configuration;
using Andy.Llm.Parsing;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Llm.Providers;

/// <summary>
/// Provider for <see href="https://openrouter.ai">OpenRouter</see>, a unified
/// gateway that fronts hundreds of models behind a single OpenAI-compatible
/// <c>POST /chat/completions</c> endpoint.
///
/// Implementation notes:
/// - OpenRouter normalizes the OpenAI chat-completions schema across every
///   upstream provider, so this is a direct <see cref="HttpClient"/>
///   implementation in the same shape as <see cref="GatewayProvider"/> rather
///   than a bespoke wire format.
/// - Model ids carry the upstream provider as a prefix
///   (e.g. <c>anthropic/claude-sonnet-4.6</c>, <c>openai/gpt-5.2</c>). Because
///   the slash collides with andy-llm's compound-alias convention, the model
///   id must live in <see cref="ProviderConfig.Model"/>, never in the provider
///   config key — the key is split on <c>/</c> for factory routing.
/// - <see cref="ProviderConfig.ApiBase"/> defaults to <see cref="DefaultApiBase"/>
///   when unset, since OpenRouter's endpoint is fixed.
/// - Optional <c>HTTP-Referer</c> and <c>X-Title</c> headers identify the app on
///   OpenRouter's leaderboard. They are sourced from
///   <see cref="ProviderConfig.AdditionalSettings"/> (<c>HttpReferer</c> /
///   <c>XTitle</c>) or the <c>OPENROUTER_HTTP_REFERER</c> /
///   <c>OPENROUTER_X_TITLE</c> environment variables, and omitted otherwise.
/// </summary>
public class OpenRouterProvider : Andy.Model.Llm.ILlmProvider
{
    internal const string DefaultApiBase = "https://openrouter.ai/api/v1";

    private readonly ILogger<OpenRouterProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _configName;
    private readonly string _defaultModel;
    private readonly string _apiBase;

    /// <inheritdoc />
    public string Name => _configName;

    /// <summary>
    /// Initializes the provider from a specific configuration entry.
    /// </summary>
    public OpenRouterProvider(
        ProviderConfig config,
        string configName,
        ILogger<OpenRouterProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configName = configName ?? "openrouter";

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new InvalidOperationException($"OpenRouter API key not configured for '{configName}'");
        }
        if (string.IsNullOrEmpty(config.Model))
        {
            throw new InvalidOperationException($"OpenRouter model not configured for '{configName}'");
        }

        _apiBase = (string.IsNullOrEmpty(config.ApiBase) ? DefaultApiBase : config.ApiBase).TrimEnd('/');
        _defaultModel = config.Model;

        _httpClient = httpClientFactory?.CreateClient("OpenRouter") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_apiBase + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);

        // Optional ranking headers. OpenRouter uses these to attribute traffic
        // to an app on its public leaderboard; they have no functional effect.
        var referer = ResolveSetting(config, "HttpReferer", "OPENROUTER_HTTP_REFERER");
        if (!string.IsNullOrEmpty(referer))
        {
            _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", referer);
        }
        var title = ResolveSetting(config, "XTitle", "OPENROUTER_X_TITLE");
        if (!string.IsNullOrEmpty(title))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Title");
            _httpClient.DefaultRequestHeaders.Add("X-Title", title);
        }

        _logger.LogInformation(
            "OpenRouter provider initialized — Config: {ConfigName}, Endpoint: {Endpoint}, Model: {Model}",
            _configName, _apiBase, _defaultModel);
    }

    /// <summary>
    /// Backward-compatibility constructor used by DI registration in
    /// <see cref="Extensions.ServiceCollectionExtensions"/>. Resolves the first
    /// OpenRouter-flavoured <see cref="ProviderConfig"/> from
    /// <see cref="LlmOptions"/> and delegates to the primary ctor. Production
    /// code paths use <c>ILlmProviderFactory.CreateProvider</c> instead, which
    /// constructs providers directly per alias.
    /// </summary>
    public OpenRouterProvider(
        IOptions<LlmOptions> options,
        ILogger<OpenRouterProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
        : this(ResolveConfig(options), ResolveConfigName(options), logger, httpClientFactory)
    {
    }

    private static ProviderConfig ResolveConfig(IOptions<LlmOptions> options)
    {
        var match = options.Value.Providers
            .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key, "openrouter", StringComparison.OrdinalIgnoreCase));
        if (match.Value != null)
        {
            return match.Value;
        }

        // Fall back to env vars so the DI-resolved provider works in tests that
        // haven't populated Providers explicitly.
        return new ProviderConfig
        {
            Provider = "openrouter",
            ApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
            ApiBase = Environment.GetEnvironmentVariable("OPENROUTER_API_BASE") ?? DefaultApiBase,
            Model = Environment.GetEnvironmentVariable("OPENROUTER_MODEL")
        };
    }

    private static string ResolveConfigName(IOptions<LlmOptions> options)
    {
        var match = options.Value.Providers
            .FirstOrDefault(p => string.Equals(p.Value.Provider ?? p.Key, "openrouter", StringComparison.OrdinalIgnoreCase));
        return match.Key ?? "openrouter";
    }

    private static string? ResolveSetting(ProviderConfig config, string settingKey, string envVar)
    {
        if (config.AdditionalSettings != null &&
            config.AdditionalSettings.TryGetValue(settingKey, out var value) &&
            value is not null)
        {
            var asString = value as string ?? value.ToString();
            if (!string.IsNullOrEmpty(asString))
            {
                return asString;
            }
        }
        return Environment.GetEnvironmentVariable(envVar);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "OpenRouter availability check failed for {ConfigName}", _configName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        var list = new List<ModelInfo>();
        try
        {
            var root = JsonNode.Parse(body);
            if (root?["data"] is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    var id = node?["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    list.Add(new ModelInfo
                    {
                        Id = id,
                        Name = node?["name"]?.GetValue<string?>() ?? id,
                        // OpenRouter has no owned_by; the upstream provider is the
                        // segment before the first slash in the model id.
                        Provider = id.Contains('/') ? id.Split('/')[0] : "openrouter"
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenRouter returned unparseable /models payload");
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, stream: false);
        using var httpResponse = await _httpClient.PostAsync(
            "chat/completions",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            cancellationToken);

        var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            // Surface API failures (401, 402, 429, 5xx, ...) to the caller with
            // the body detail rather than synthesising a fake assistant
            // response, matching the convention established in CerebrasProvider
            // and AnthropicProvider.
            _logger.LogError("OpenRouter non-success {Status} for {ConfigName}: {Body}",
                (int)httpResponse.StatusCode, _configName, Truncate(responseText));
            throw new InvalidOperationException(
                $"OpenRouter request failed (status {(int)httpResponse.StatusCode}): {Truncate(responseText)}");
        }

        var root = JsonNode.Parse(responseText)
            ?? throw new InvalidOperationException("OpenRouter returned an empty response body.");

        var choice = root["choices"]?[0];
        var messageNode = choice?["message"];
        var content = messageNode?["content"]?.GetValue<string?>() ?? string.Empty;
        var finishReason = choice?["finish_reason"]?.GetValue<string?>();
        var modelUsed = root["model"]?.GetValue<string?>() ?? request.Model ?? _defaultModel;

        var toolCalls = ReadToolCalls(messageNode?["tool_calls"]);

        var assistant = new Message
        {
            Role = Role.Assistant,
            Content = content,
            ToolCalls = toolCalls
        };

        return new LlmResponse
        {
            AssistantMessage = assistant,
            FinishReason = finishReason,
            Model = modelUsed,
            Usage = ReadUsage(root["usage"])
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, stream: true);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var httpResponse = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            yield return new LlmStreamResponse
            {
                IsComplete = true,
                Error = $"OpenRouter request failed (status {(int)httpResponse.StatusCode}): {Truncate(errorBody)}"
            };
            yield break;
        }

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? finishReason = null;
        LlmUsage? usage = null;
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line[6..];
            if (payload == "[DONE]")
            {
                break;
            }

            JsonNode? chunk;
            try
            { chunk = JsonNode.Parse(payload); }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Unparseable OpenRouter SSE chunk, skipping");
                continue;
            }
            if (chunk is null)
            {
                continue;
            }

            var delta = chunk["choices"]?[0]?["delta"];
            var chunkFinish = chunk["choices"]?[0]?["finish_reason"]?.GetValue<string?>();
            if (!string.IsNullOrEmpty(chunkFinish))
            {
                finishReason = chunkFinish;
            }

            if (chunk["usage"] is { } u)
            {
                usage = ReadUsage(u);
            }

            if (delta?["content"]?.GetValue<string?>() is { Length: > 0 } text)
            {
                yield return new LlmStreamResponse
                {
                    Delta = new Message { Role = Role.Assistant, Content = text },
                    IsComplete = false
                };
            }

            if (delta?["tool_calls"] is JsonArray tcArray)
            {
                foreach (var tc in tcArray)
                {
                    if (tc is null)
                    {
                        continue;
                    }

                    var index = tc["index"]?.GetValue<int?>() ?? 0;
                    if (!toolCallAccumulators.TryGetValue(index, out var acc))
                    {
                        acc = new ToolCallAccumulator();
                        toolCallAccumulators[index] = acc;
                    }
                    if (tc["id"]?.GetValue<string?>() is { Length: > 0 } id)
                    {
                        acc.Id = id;
                    }

                    if (tc["function"]?["name"]?.GetValue<string?>() is { Length: > 0 } name)
                    {
                        acc.Name = name;
                    }

                    if (tc["function"]?["arguments"]?.GetValue<string?>() is { Length: > 0 } argFragment)
                    {
                        acc.Arguments.Append(argFragment);
                    }
                }
            }
        }

        foreach (var acc in toolCallAccumulators.Values.OrderBy(a => a.Name))
        {
            var assembled = new ToolCall
            {
                Id = acc.Id ?? Guid.NewGuid().ToString("N"),
                Name = acc.Name ?? string.Empty,
                ArgumentsJson = ToolArgumentJsonRepair.Normalize(acc.Arguments.ToString())
            };
            yield return new LlmStreamResponse
            {
                Delta = new Message { Role = Role.Assistant, ToolCalls = new List<ToolCall> { assembled } },
                IsComplete = false
            };
        }

        yield return new LlmStreamResponse
        {
            IsComplete = true,
            FinishReason = finishReason,
            Usage = usage
        };
    }

    private JsonObject BuildRequestBody(LlmRequest request, bool stream)
    {
        var messagesArray = new JsonArray();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        foreach (var msg in request.Messages)
        {
            foreach (var node in ConvertMessage(msg))
            {
                messagesArray.Add(node);
            }
        }

        var obj = new JsonObject
        {
            ["model"] = string.IsNullOrEmpty(request.Model) ? _defaultModel : request.Model,
            ["messages"] = messagesArray,
            ["stream"] = stream
        };

        if (request.Config?.Temperature is { } temp)
        {
            obj["temperature"] = (double)temp;
        }

        if (request.Config?.MaxTokens is int max && max > 0)
        {
            obj["max_tokens"] = max;
        }

        if (request.Config?.TopP is { } topP && topP > 0m)
        {
            obj["top_p"] = (double)topP;
        }

        if (request.Tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var tool in request.Tools)
            {
                toolsArray.Add(ConvertTool(tool));
            }
            obj["tools"] = toolsArray;
        }

        // Merge caller-supplied provider-specific fields verbatim — OpenRouter's `provider` routing,
        // `models` fallback array, `reasoning`, `transforms`, `response_format`, and any other
        // request-body field. Serialized as-is so nested dictionaries/arrays keep their shape.
        // ExtraBody wins on key collisions: the caller set it explicitly.
        if (request.ExtraBody is { } extra)
        {
            foreach (var kvp in extra)
            {
                obj[kvp.Key] = kvp.Value is null
                    ? null
                    : JsonSerializer.SerializeToNode(kvp.Value);
            }
        }

        return obj;
    }

    private static IEnumerable<JsonObject> ConvertMessage(Message message)
    {
        var role = message.Role switch
        {
            Role.System => "system",
            Role.User => "user",
            Role.Assistant => "assistant",
            Role.Tool => "tool",
            _ => "user"
        };

        // Tool results are emitted as one `role: tool` message per result,
        // following the OpenAI convention.
        if (message.ToolResults.Count > 0)
        {
            foreach (var tr in message.ToolResults)
            {
                yield return new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = tr.CallId,
                    ["content"] = tr.ResultJson
                };
            }
            yield break;
        }

        var node = new JsonObject
        {
            ["role"] = role,
            ["content"] = message.Content
        };

        if (message.ToolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = string.IsNullOrEmpty(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                    }
                });
            }
            node["tool_calls"] = calls;
        }

        yield return node;
    }

    private static JsonObject ConvertTool(ToolDeclaration tool)
    {
        var parameters = JsonSerializer.SerializeToNode(tool.Parameters) ?? new JsonObject();
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = parameters
            }
        };
    }

    private static List<ToolCall> ReadToolCalls(JsonNode? node)
    {
        var list = new List<ToolCall>();
        if (node is not JsonArray arr)
        {
            return list;
        }

        foreach (var tc in arr)
        {
            var id = tc?["id"]?.GetValue<string?>() ?? Guid.NewGuid().ToString("N");
            var name = tc?["function"]?["name"]?.GetValue<string?>() ?? string.Empty;
            var args = tc?["function"]?["arguments"]?.GetValue<string?>() ?? "{}";
            list.Add(new ToolCall { Id = id, Name = name, ArgumentsJson = ToolArgumentJsonRepair.Normalize(args) });
        }
        return list;
    }

    private static LlmUsage? ReadUsage(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var prompt = node["prompt_tokens"]?.GetValue<int?>() ?? 0;
        var completion = node["completion_tokens"]?.GetValue<int?>() ?? 0;
        var total = node["total_tokens"]?.GetValue<int?>() ?? (prompt + completion);
        if (prompt == 0 && completion == 0 && total == 0)
        {
            return null;
        }

        return new LlmUsage
        {
            PromptTokens = prompt,
            CompletionTokens = completion,
            TotalTokens = total
        };
    }

    private static string Truncate(string s, int max = 512)
        => s.Length <= max ? s : s[..max] + "…";

    private sealed class ToolCallAccumulator
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
