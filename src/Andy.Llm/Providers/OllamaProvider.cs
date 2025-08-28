using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.Llm.Abstractions;
using Andy.Llm.Configuration;
using Andy.Llm.Models;
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

    public OllamaProvider(
        IOptions<LlmOptions> options,
        ILogger<OllamaProvider> logger,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        
        // Load configuration
        var config = LoadConfiguration(options.Value);
        
        _apiBase = config.ApiBase ?? Environment.GetEnvironmentVariable("OLLAMA_API_BASE") ?? "http://localhost:11434";
        _defaultModel = config.Model ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "llama2";
        
        // Create HTTP client
        _httpClient = httpClientFactory?.CreateClient("Ollama") ?? new HttpClient();
        _httpClient.BaseAddress = new Uri(_apiBase);
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Longer timeout for local models
        
        _logger.LogInformation("Ollama provider initialized with endpoint: {Endpoint}, default model: {Model}", 
            _apiBase, _defaultModel);
    }

    public string Name => "ollama";

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
            
            var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
                cancellationToken: cancellationToken);
            
            if (ollamaResponse == null)
            {
                throw new InvalidOperationException("Received null response from Ollama");
            }
            
            return ConvertResponse(ollamaResponse, request.Model ?? _defaultModel);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Ollama request failed");
            
            // Check if it's a 404 error (model not found)
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                var modelName = request.Model ?? _defaultModel;
                throw new InvalidOperationException(
                    $"Model '{modelName}' not found in Ollama. " +
                    $"Please ensure the model is installed with 'ollama pull {modelName}' " +
                    $"or set OLLAMA_MODEL environment variable to an installed model.", ex);
            }
            
            throw new InvalidOperationException($"Ollama request failed: {ex.Message}", ex);
        }
    }

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
            yield break;
        
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
                    continue;
                
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
                    continue;
                
                // Yield content
                if (!string.IsNullOrEmpty(streamResponse.Message?.Content))
                {
                    yield return new LlmStreamResponse
                    {
                        TextDelta = streamResponse.Message.Content,
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
        if (request.Temperature.HasValue || request.MaxTokens.HasValue)
        {
            ollamaRequest.Options = new OllamaOptions();
            
            if (request.Temperature.HasValue)
                ollamaRequest.Options.Temperature = request.Temperature.Value;
                
            if (request.MaxTokens.HasValue)
                ollamaRequest.Options.NumPredict = request.MaxTokens.Value;
        }
        
        return ollamaRequest;
    }

    private LlmResponse ConvertResponse(OllamaChatResponse ollamaResponse, string model)
    {
        return new LlmResponse
        {
            Content = ollamaResponse.Message?.Content ?? string.Empty,
            Model = model,
            FunctionCalls = new List<FunctionCall>(), // Ollama doesn't support function calling yet
            TokensUsed = ollamaResponse.PromptEvalCount + ollamaResponse.EvalCount,
            Usage = new TokenUsage
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