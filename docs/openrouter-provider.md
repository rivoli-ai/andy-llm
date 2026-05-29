# OpenRouter provider

`OpenRouterProvider` lets a service reach [OpenRouter](https://openrouter.ai) — a unified, OpenAI-compatible gateway that fronts hundreds of models (OpenAI, Anthropic, Google, Meta, Qwen, DeepSeek, …) behind a single API key. OpenRouter normalizes the OpenAI chat-completions schema across every upstream model, so you learn one wire format and switch models by changing a string.

## When to use it

- **One key, many models** — try Claude, GPT, Gemini, Llama, and Qwen without signing up for each vendor.
- **Free models** — OpenRouter publishes tool-capable `:free` variants that cost nothing, ideal for examples, demos, and CI smoke tests:
  - `openai/gpt-oss-20b:free` (default — most reliably available)
  - `qwen/qwen3-next-80b-a3b-instruct:free` (Qwen)
  - `moonshotai/kimi-k2.6:free` (Kimi)
  - `z-ai/glm-4.5-air:free` (GLM)

  The `examples/OpenRouter/appsettings.json` registers all four as `openrouter/free`, `openrouter/qwen-free`, `openrouter/kimi-free`, and `openrouter/glm-free`.
- **Fallback / routing** — register it alongside direct providers (`openai`, `cerebras`, `anthropic`) and let `CreateAvailableProviderAsync()` pick the highest-priority healthy one.

> **Free-tier note:** the shared free pool is aggressively rate-limited (HTTP 429 — some models, such as the free Qwen variants, cap at ~8 requests/minute) and `:free` slugs are occasionally retired (HTTP 404). The provider surfaces both as errors; callers should retry on 429 or fall back to another model. The example and integration tests default to `openai/gpt-oss-20b:free`, which has been the most reliably available — switch to a Qwen free slug via `OPENROUTER_MODEL` if you prefer.

## Configuration

```json
{
  "Llm": {
    "DefaultProvider": "openrouter/free",
    "Providers": {
      "openrouter/free": {
        "Provider": "openrouter",
        "ApiBase": "https://openrouter.ai/api/v1",
        "ApiKey": "${OPENROUTER_API_KEY}",
        "Model": "openai/gpt-oss-20b:free",
        "Enabled": true,
        "AdditionalSettings": {
          "HttpReferer": "https://github.com/rivoli-ai/andy-llm",
          "XTitle": "Andy.Llm"
        }
      },
      "openrouter/claude-sonnet": {
        "Provider": "openrouter",
        "ApiBase": "https://openrouter.ai/api/v1",
        "ApiKey": "${OPENROUTER_API_KEY}",
        "Model": "anthropic/claude-sonnet-4.6",
        "Enabled": true
      }
    }
  }
}
```

### The `/` in model ids — important

OpenRouter model ids carry the upstream provider as a prefix (`anthropic/claude-sonnet-4.6`, `openai/gpt-5.2`). andy-llm **also** uses `/` as its compound-alias separator and routes by splitting the config key on `/`. To avoid a collision:

- Put the slashed model id in **`Model`**, never in the provider config key.
- Keep `Provider` set to `"openrouter"` so the factory routes by that field rather than by splitting the key.

So `"openrouter/claude-sonnet"` (the key) routes to the OpenRouter provider, and `"anthropic/claude-sonnet-4.6"` (the `Model`) is forwarded to OpenRouter untouched.

`ApiBase` is optional — it defaults to `https://openrouter.ai/api/v1` when omitted.

### Optional ranking headers

OpenRouter can attribute traffic to your app on its public leaderboard via two headers. They have no functional effect and are omitted unless configured:

| Header | `AdditionalSettings` key | Environment variable |
|---|---|---|
| `HTTP-Referer` | `HttpReferer` | `OPENROUTER_HTTP_REFERER` |
| `X-Title` | `XTitle` | `OPENROUTER_X_TITLE` |

### Environment variable fallbacks

| Variable | Purpose |
|---|---|
| `OPENROUTER_API_KEY` | Your OpenRouter API key (from <https://openrouter.ai/keys>) |
| `OPENROUTER_API_BASE` | Base URL (optional; defaults to `https://openrouter.ai/api/v1`) |
| `OPENROUTER_MODEL` | Default `provider/model` slug when a request doesn't specify one |
| `OPENROUTER_HTTP_REFERER` | Optional `HTTP-Referer` ranking header |
| `OPENROUTER_X_TITLE` | Optional `X-Title` ranking header |

When only `OPENROUTER_API_KEY` is set (no `appsettings.json`), the provider is created automatically in "legacy mode" via `ConfigureLlmFromEnvironment()`.

## What the provider does

- `POST /chat/completions` to OpenRouter, forwarding messages, tools, temperature, `max_tokens`, and `top_p` in the standard OpenAI shape.
- **Streaming**: relays SSE chunks, accumulates partial tool-call arguments across deltas, and emits one terminal `LlmStreamResponse` with `IsComplete=true`, `FinishReason`, and final `Usage`. Pre-stream HTTP errors surface as a single terminal frame with `Error` set.
- **Errors**: non-success responses (401/402/429/5xx) throw `InvalidOperationException` with the status code and response body rather than synthesising a fake assistant message.
- `GET /models` powers `ListModelsAsync()`; the upstream provider is derived from the model id prefix.

## Quick start

```bash
export OPENROUTER_API_KEY="sk-or-..."     # https://openrouter.ai/keys
dotnet run --project examples/OpenRouter   # uses the free Qwen model — no cost
```

```csharp
var provider = factory.CreateProvider("openrouter/free");
var response = await provider.CompleteAsync(new LlmRequest
{
    Messages = new List<Message> { new() { Role = Role.User, Content = "Hello!" } },
    Config = new LlmClientConfig { Model = "openai/gpt-oss-20b:free", MaxTokens = 64 }
});
```

## Integration tests

`tests/Andy.Llm.Tests/Providers/OpenRouterIntegrationTests.cs` makes real API calls. They no-op when `OPENROUTER_API_KEY` is unset, and default to the free `openai/gpt-oss-20b:free` model (override with `OPENROUTER_MODEL`). Run them with:

```bash
export OPENROUTER_API_KEY="sk-or-..."
dotnet test --filter "FullyQualifiedName~OpenRouterIntegrationTests"
```
