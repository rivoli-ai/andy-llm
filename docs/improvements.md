# andy-llm: Function-Calling and Grounding Plan

## Goals
- Enable reliable function-calling (tool call) with strict JSON schemas.
- Ensure tool results are fed back as raw JSON, referenced by call_id.
- Support streaming function calls coherently across providers.

## Scope (andy-llm)
- Public API changes to `LlmRequest`/`LlmResponse` and streaming deltas.
- Provider adapters (Cerebras/Qwen/OpenAI/Anthropic) to unify function-calling.
- Utilities for call_id continuity and stable JSON serialization.

## Changes

### Function-Calling API
- Add `Functions` to `LlmRequest`:
  - name: string (tool id)
  - description: string
  - parameters: JSON schema (object, properties, required)
- Add `FunctionCall` to `LlmResponse` and streaming deltas:
  - id: string (call_id)
  - name: string (tool id)
  - arguments: Dictionary<string, object?>

### Streaming Support
- Ensure streaming includes partial function call deltas and a clear completion signal:
  - text deltas and function call deltas must be mutually consistent.
  - mark final chunk with `IsComplete = true` and `FinishReason`.

### Provider Adapters
- Implement function-calling translation per provider:
  - Map our `Functions` to provider-native function/tool schemas.
  - Map provider function call events back into `FunctionCall` deltas.
- Normalize provider names to keep Qwen parsing path for Qwen models even when hosted by Cerebras.

### Serialization Utilities
- Add JSON helpers:
  - CamelCase serialization for tool Data objects.
  - Stable stringification for tool Data when types are unknown (fallback to object graph→JSON).
- Provide a “safe string” mode for `arguments` when providers return strings instead of objects.

### Retry/Validation Hooks
- Expose an optional “parameter-correction suggestion” event:
  - Given a `FunctionCall` and tool metadata, suggest remapped parameter names and minimal corrections.
  - Caller (CLI) decides to retry.

## Tests
- Unit: function schema build, camelCase serialization, argument parsing (strings/objects).
- Integration (mock providers): function call roundtrips, streaming completion, multiple calls sequencing.
- Contract: ensure `LlmRequest` + `Functions` produce structured function calls for each adapter.

---

## Progress (in repo)
- Completed:
  - `LlmRequest.Functions` alias (maps to `Tools`).
  - `FunctionCall.ArgumentsJson` added and populated in providers.
  - Streaming `FinishReason` set on final chunk; partial function-call deltas emitted.
  - JSON helpers: camelCase options and stable stringification.
  - Basic parameter-correction suggestion service.
  - Provider updates: OpenAI, Azure OpenAI, Cerebras; Qwen normalization in Cerebras model metadata.
  - Tests added/updated; all pass (Azure integration tests skipped by design).
- Pending:
  - Anthropic adapter alignment (if/when added) for function-calling path.
  - Additional integration tests for multi-call sequencing across providers.
  - Optional CLI hook wiring for parameter-correction retry flow.