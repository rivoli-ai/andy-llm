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

namespace Andy.Llm.Providers;

/// <summary>
/// Provider that speaks OpenAI chat-completions wire format to an
/// Andy Models gateway (<see href="https://github.com/rivoli-ai/andy-models"/>).
/// The gateway owns per-tenant provider keys, cost tracking, and routing;
/// this client just forwards requests authenticated with a single tenant
/// token. Model ids are canonical catalog slugs like
/// <c>anthropic/claude-sonnet-4-5</c>; the gateway handles rewriting to
/// upstream ids.
/// </summary>
public class GatewayProvider : Andy.Model.Llm.ILlmProvider
{
    private readonly ILogger<GatewayProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _configName;
    private readonly string _defaultModel;
    private readonly string _apiBase;

    /// <summary>
    /// Initializes the provider from a specific configuration entry.
    /// </summary>
    public GatewayProvider(
        ProviderConfig config,
        string configName,
        ILogger<GatewayProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configName = configName ?? "gateway";

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new InvalidOperationException($"Gateway API key not configured for '{configName}'");
        }
        if (string.IsNullOrEmpty(config.ApiBase))
        {
            throw new InvalidOperationException($"Gateway API base URL not configured for '{configName}'");
        }
        if (string.IsNullOrEmpty(config.Model))
        {
            throw new InvalidOperationException($"Gateway default model not configured for '{configName}'");
        }

        _apiBase = config.ApiBase.TrimEnd('/');
        _defaultModel = config.Model;

        _httpClient = httpClientFactory?.CreateClient("Gateway") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_apiBase + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);

        _logger.LogInformation(
            "Gateway provider initialized — Config: {ConfigName}, Endpoint: {Endpoint}, Model: {Model}",
            _configName, _apiBase, _defaultModel);
    }

    /// <inheritdoc />
    public string Name => _configName;

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("v1/models", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Gateway availability check failed for {ConfigName}", _configName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("v1/models", cancellationToken);
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
                    if (string.IsNullOrEmpty(id)) continue;
                    list.Add(new ModelInfo
                    {
                        Id = id,
                        Name = id,
                        Provider = node?["owned_by"]?.GetValue<string?>() ?? _configName
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Gateway returned unparseable /v1/models payload");
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request, stream: false);
        using var httpResponse = await _httpClient.PostAsync(
            "v1/chat/completions",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            cancellationToken);

        var responseText = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Gateway non-success {Status} for {ConfigName}: {Body}",
                (int)httpResponse.StatusCode, _configName, Truncate(responseText));
            httpResponse.EnsureSuccessStatusCode();
        }

        var root = JsonNode.Parse(responseText)
            ?? throw new InvalidOperationException("Gateway returned an empty response body.");

        var choice = root["choices"]?[0];
        var messageNode = choice?["message"];
        var content = messageNode?["content"]?.GetValue<string?>() ?? string.Empty;
        var finishReason = choice?["finish_reason"]?.GetValue<string?>();
        var modelUsed = root["model"]?.GetValue<string?>() ?? request.Model;

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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var httpResponse = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        httpResponse.EnsureSuccessStatusCode();

        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? finishReason = null;
        LlmUsage? usage = null;
        var toolCallAccumulators = new Dictionary<int, ToolCallAccumulator>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line[6..];
            if (payload == "[DONE]") break;

            JsonNode? chunk;
            try { chunk = JsonNode.Parse(payload); }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Unparseable gateway SSE chunk, skipping");
                continue;
            }
            if (chunk is null) continue;

            var delta = chunk["choices"]?[0]?["delta"];
            var chunkFinish = chunk["choices"]?[0]?["finish_reason"]?.GetValue<string?>();
            if (!string.IsNullOrEmpty(chunkFinish)) finishReason = chunkFinish;

            if (chunk["usage"] is { } u) usage = ReadUsage(u);

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
                    if (tc is null) continue;
                    var index = tc["index"]?.GetValue<int?>() ?? 0;
                    if (!toolCallAccumulators.TryGetValue(index, out var acc))
                    {
                        acc = new ToolCallAccumulator();
                        toolCallAccumulators[index] = acc;
                    }
                    if (tc["id"]?.GetValue<string?>() is { Length: > 0 } id) acc.Id = id;
                    if (tc["function"]?["name"]?.GetValue<string?>() is { Length: > 0 } name) acc.Name = name;
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
            var converted = ConvertMessage(msg);
            foreach (var node in converted)
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

        if (request.Config?.Temperature is { } temp) obj["temperature"] = (double)temp;
        if (request.Config?.MaxTokens is int max && max > 0) obj["max_tokens"] = max;
        if (request.Config?.TopP is { } topP && topP > 0m) obj["top_p"] = (double)topP;

        if (request.Tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var tool in request.Tools)
            {
                toolsArray.Add(ConvertTool(tool));
            }
            obj["tools"] = toolsArray;
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
        if (node is not JsonArray arr) return list;
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
        if (node is null) return null;
        var prompt = node["prompt_tokens"]?.GetValue<int?>() ?? 0;
        var completion = node["completion_tokens"]?.GetValue<int?>() ?? 0;
        var total = node["total_tokens"]?.GetValue<int?>() ?? (prompt + completion);
        if (prompt == 0 && completion == 0 && total == 0) return null;
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
