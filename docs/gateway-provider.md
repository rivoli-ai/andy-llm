# Gateway provider (`andy-models`)

`GatewayProvider` lets a service speak OpenAI chat-completions wire format to an [`andy-models`](https://github.com/rivoli-ai/andy-models) gateway instead of talking to vendors directly. The gateway owns provider API keys, routing, pricing snapshots, and the usage ledger; callers just present a single tenant bearer token.

## When to switch

- **Switch** as your default once the gateway is reachable from your deployment — you stop managing per-vendor keys and get cost/usage tracking in one place.
- **Keep the direct provider** (`openai`, `cerebras`, `ollama`, `azure`) for:
  - Offline or Conductor-embedded modes where the gateway isn't running.
  - Hot paths where you can't tolerate an additional network hop.

Both paths coexist: you can register a `gateway` provider alongside a direct `openai` provider and select between them per call or via `Llm:DefaultProvider`.

## Configuration

```json
{
  "Llm": {
    "DefaultProvider": "gateway",
    "Providers": {
      "gateway": {
        "Provider": "gateway",
        "ApiBase": "https://andy-models.internal:5120",
        "ApiKey": "${ANDY_MODELS_API_KEY}",
        "Model": "openai/gpt-4o-mini"
      },
      "openai/direct": {
        "Provider": "openai",
        "ApiKey": "${OPENAI_API_KEY}",
        "ApiBase": "https://api.openai.com/v1",
        "Model": "gpt-4o-mini",
        "Priority": 10
      }
    }
  }
}
```

The `Model` under the gateway config is the **canonical catalog slug** (e.g. `openai/gpt-4o-mini`, `anthropic/claude-sonnet-4-5`, `openrouter/anthropic/claude-sonnet-4-5`). The gateway handles rewriting to the upstream id and routing to the actual vendor endpoint.

### Environment variable fallbacks

| Variable | Purpose |
|---|---|
| `ANDY_MODELS_API_BASE` | Base URL of the gateway (e.g. `https://andy-models.internal:5120`) |
| `ANDY_MODELS_API_KEY` | Bearer token accepted by the gateway |
| `ANDY_MODELS_MODEL` | Default catalog slug when a request doesn't specify one |

## What the provider does

- `POST /v1/chat/completions` to the gateway, forwarding messages, tools, temperature, `max_tokens`, and `top_p` in the standard OpenAI shape.
- Streaming: relays SSE chunks, accumulates partial tool-call arguments across deltas, emits one terminal `LlmStreamResponse` with `IsComplete=true`, `FinishReason`, and final `Usage`.
- `GET /v1/models` returns the `ModelInfo` list the gateway's RBAC scope permits; use it for model discovery.

## Migration steps

1. **Add the `gateway` provider config** in your service's `appsettings.json` (see above) without yet switching the default.
2. **Verify** the new provider works by instantiating it via `ILlmProviderFactory.CreateProvider("gateway")` in a feature-flagged code path.
3. **Flip `Llm:DefaultProvider`** to `"gateway"`. Leave the direct `openai/direct` (or equivalent) entry registered as a fallback; `CreateAvailableProviderAsync()` picks the highest-Priority healthy provider, so a down gateway automatically falls back.
4. **Stop setting vendor keys** (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, …) on the service. The gateway holds them.
5. **Model references** change from vendor-native ids (`gpt-4o-mini`) to catalog slugs (`openai/gpt-4o-mini`). A one-liner in each consumer:
   ```csharp
   var request = new LlmRequest {
       Messages = messages,
       Config = new LlmClientConfig { Model = "openai/gpt-4o-mini" }  // was "gpt-4o-mini"
   };
   ```

## Token forwarding (future)

Today the provider uses a single service-level bearer token (`Proxy:ProviderKeys:<slug>` equivalent on the gateway side authorizes the service). A follow-up will add a pluggable `IGatewayTokenProvider` so per-user tokens from the caller's JWT can be forwarded unchanged — needed when the gateway enforces per-user RBAC.
