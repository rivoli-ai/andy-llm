using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Llm.Providers;
using Andy.Llm.Configuration;
using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.Llm.Providers;

/// <summary>
/// Provider for Ollama local LLM server.
/// </summary>
public class OllamaProvider : ILlmProvider
{
    private readonly ILogger<OllamaProvider> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _apiBase;
    private readonly string _defaultModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaProvider"/> class.
    /// </summary>
    /// <param name="options">The LLM options containing Ollama configuration.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="httpClientFactory">Optional HTTP client factory for creating HTTP clients.</param>
    public OllamaProvider(
        IOptions<LlmOptions> options,
        ILogger<OllamaProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;

        // Load configuration
        var config = LoadConfiguration(options.Value);

        _apiBase = config.ApiBase ?? Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434";
        _defaultModel = config.Model ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "gpt-oss:20b";

        // Create HTTP client
        _httpClient = httpClientFactory?.CreateClient("Ollama") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_apiBase);
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for local models

        _logger.LogInformation("Ollama provider initialized with endpoint: {Endpoint}, default model: {Model}",
            _apiBase, _defaultModel);
    }

    /// <summary>
    /// Gets the name of the provider.
    /// </summary>
    public string Name => "ollama";

    /// <summary>
    /// Checks if the Ollama service is available.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the service is available, false otherwise.</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ollama availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Completes a chat request using Ollama.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The LLM response.</returns>
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var ollamaRequest = CreateOllamaRequest(request, stream: false);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/chat",
                ollamaRequest,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogDebug("Ollama raw response: {Response}", responseContent);

            var ollamaResponse = System.Text.Json.JsonSerializer.Deserialize<OllamaChatResponse>(responseContent);

            if (ollamaResponse == null)
            {
                throw new InvalidOperationException("Received null response from Ollama");
            }

            _logger.LogDebug("Ollama parsed message content: {Content}", ollamaResponse.Message?.Content ?? "(null)");

            return ConvertResponse(ollamaResponse, request.Model ?? _defaultModel);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("Ollama request failed: {Message}", ex.Message);

            // Check if it's a 404 error (model not found)
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var modelName = request.Model ?? _defaultModel;
                throw new InvalidOperationException(
                    $"Model '{modelName}' not found in Ollama. " +
                    $"Please ensure the model is installed with 'ollama pull {modelName}' " +
                    $"or set OLLAMA_MODEL environment variable to an installed model (e.g., gpt-oss:20b, phi4:latest).", ex);
            }

            throw new InvalidOperationException($"Ollama request failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Lists available models from Ollama.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of available models.</returns>
    public async Task<IEnumerable<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var tagsResponse = JsonSerializer.Deserialize<OllamaTagsResponse>(content);

            if (tagsResponse?.Models == null)
            {
                return Enumerable.Empty<ModelInfo>();
            }

            return tagsResponse.Models.Select(model => new ModelInfo
            {
                Id = model.Name ?? string.Empty,
                Name = model.Name ?? string.Empty,
                Provider = "ollama",
                Description = model.Details?.Format ?? "Local model",
                UpdatedAt = ParseDateTime(model.ModifiedAt) is DateTime dt ? new DateTimeOffset(dt) : null,
                Family = model.Details?.Family,
                ParameterSize = model.Details?.ParameterSize,
                Metadata = new Dictionary<string, object>
                {
                    ["size"] = FormatBytes(model.Size ?? 0),
                    ["digest"] = model.Digest ?? string.Empty,
                    ["quantization"] = model.Details?.QuantizationLevel ?? "unknown"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Ollama models");
            return Enumerable.Empty<ModelInfo>();
        }
    }

    private static DateTime? ParseDateTime(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
        {
            return null;
        }

        if (DateTime.TryParse(dateStr, out var date))
        {
            return date;
        }

        return null;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Streams a chat completion response from Ollama.
    /// </summary>
    /// <param name="request">The LLM request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An async enumerable of stream responses.</returns>
    public async IAsyncEnumerable<LlmStreamResponse> StreamCompleteAsync(
        LlmRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ollamaRequest = CreateOllamaRequest(request, stream: true);

        HttpResponseMessage? response = null;
        Stream? stream = null;
        StreamReader? reader = null;
        string? errorMessage = null;

        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(ollamaRequest),
                Encoding.UTF8,
                "application/json");

            response = await _httpClient.PostAsync("/api/chat", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            reader = new StreamReader(stream);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Ollama streaming request failed");

            // Check if it's a 404 error (model not found)
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var modelName = request.Model ?? _defaultModel;
                errorMessage = $"Model '{modelName}' not found in Ollama. " +
                    $"Please ensure the model is installed with 'ollama pull {modelName}' " +
                    $"or set OLLAMA_MODEL environment variable to an installed model.";
            }
            else
            {
                errorMessage = ex.Message;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Ollama streaming cancelled");
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

        try
        {
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                string? line = null;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Ollama streaming cancelled during read");
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                OllamaStreamResponse? streamResponse;
                try
                {
                    streamResponse = JsonSerializer.Deserialize<OllamaStreamResponse>(line);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse Ollama stream response: {Line}", line);
                    continue;
                }

                if (streamResponse == null)
                {
                    continue;
                }

                // Yield content
                if (!string.IsNullOrEmpty(streamResponse.Message?.Content))
                {
                    yield return new LlmStreamResponse
                    {
                        Delta = new Message { Role = Role.Assistant, Content = streamResponse.Message.Content },
                        IsComplete = false
                    };
                }

                // Check if complete
                if (streamResponse.Done)
                {
                    yield return new LlmStreamResponse
                    {
                        IsComplete = true
                    };
                    break;
                }
            }
        }
        finally
        {
            reader?.Dispose();
            stream?.Dispose();
            response?.Dispose();
        }
    }

    private ProviderConfig LoadConfiguration(LlmOptions options)
    {
        if (options.Providers.TryGetValue("ollama", out var config))
        {
            return config;
        }

        // Fall back to environment variables
        return new ProviderConfig
        {
            ApiBase = Environment.GetEnvironmentVariable("OLLAMA_API_BASE"),
            Model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
        };
    }

    private OllamaChatRequest CreateOllamaRequest(LlmRequest request, bool stream)
    {
        var ollamaRequest = new OllamaChatRequest
        {
            Model = request.Model ?? _defaultModel,
            Messages = new List<OllamaMessage>(),
            Stream = stream
        };

        // Add system message if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            ollamaRequest.Messages.Add(new OllamaMessage
            {
                Role = "system",
                Content = request.SystemPrompt
            });
        }

        // Convert messages
        foreach (var message in request.Messages)
        {
            var content = string.Join(" ", message.Parts.OfType<TextPart>().Select(p => p.Text));

            ollamaRequest.Messages.Add(new OllamaMessage
            {
                Role = message.Role switch
                {
                    MessageRole.System => "system",
                    MessageRole.User => "user",
                    MessageRole.Assistant => "assistant",
                    MessageRole.Tool => "assistant", // Ollama doesn't have a separate tool role
                    _ => "user"
                },
                Content = content
            });
        }

        // Set options
        ollamaRequest.Options = new OllamaOptions();
        ollamaRequest.Options.Temperature = (double)request.Temperature;
        ollamaRequest.Options.NumPredict = request.MaxTokens;

        return ollamaRequest;
    }

    private LlmResponse ConvertResponse(OllamaChatResponse ollamaResponse, string model)
    {
        // Some models (like gpt-oss) return content in 'thinking' field instead of 'content'
        var content = ollamaResponse.Message?.Content;
        if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(ollamaResponse.Message?.Thinking))
        {
            content = ollamaResponse.Message.Thinking;
            _logger.LogDebug("Using 'thinking' field as content for model {Model}", model);
        }

        return new LlmResponse
        {
            AssistantMessage = new Message
            {
                Role = Role.Assistant,
                Content = content ?? string.Empty
            },
            Model = model,
            Usage = new LlmUsage
            {
                PromptTokens = ollamaResponse.PromptEvalCount ?? 0,
                CompletionTokens = ollamaResponse.EvalCount ?? 0,
                TotalTokens = (ollamaResponse.PromptEvalCount ?? 0) + (ollamaResponse.EvalCount ?? 0)
            },
            Metadata = new Dictionary<string, object>
            {
                ["total_duration"] = ollamaResponse.TotalDuration ?? 0,
                ["load_duration"] = ollamaResponse.LoadDuration ?? 0,
                ["eval_duration"] = ollamaResponse.EvalDuration ?? 0
            }
        };
    }
}

// Ollama API Models

internal class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<OllamaMessage> Messages { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("options")]
    public OllamaOptions? Options { get; set; }
}

internal class OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }  // Some models like gpt-oss use this field
}

internal class OllamaOptions
{
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("num_predict")]
    public int? NumPredict { get; set; }

    [JsonPropertyName("top_k")]
    public int? TopK { get; set; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; set; }
}

internal class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("total_duration")]
    public long? TotalDuration { get; set; }

    [JsonPropertyName("load_duration")]
    public long? LoadDuration { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    [JsonPropertyName("prompt_eval_duration")]
    public long? PromptEvalDuration { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; set; }
}

internal class OllamaStreamResponse : OllamaChatResponse
{
    // Inherits all properties from OllamaChatResponse
}

internal class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModelInfo>? Models { get; set; }
}

internal class OllamaModelInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("modified_at")]
    public string? ModifiedAt { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    [JsonPropertyName("details")]
    public OllamaModelDetails? Details { get; set; }
}

internal class OllamaModelDetails
{
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("family")]
    public string? Family { get; set; }

    [JsonPropertyName("families")]
    public List<string>? Families { get; set; }

    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }
}
