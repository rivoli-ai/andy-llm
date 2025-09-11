### andy-llm Implementation Status

#### Overview
This document tracks the implementation progress for function-calling and grounding improvements.

#### API Additions (non-breaking)
- `LlmRequest.Functions` (alias of `Tools`) to align with common terminology.
- `FunctionCall.ArgumentsJson` to preserve raw JSON arguments from providers.
- `LlmStreamResponse.FinishReason` for final streaming chunk reason.

#### Streaming Behavior
- Providers emit partial function-call deltas as arguments are streamed.
- Final streaming chunk sets `IsComplete = true` with `FinishReason`.
- Mixed streams (text deltas + tool deltas) are supported.

#### Provider Mapping
- OpenAI: maps tools and function-call events; `ArgumentsJson` preserved.
- Azure OpenAI: maps tool call updates; final chunk sets `FinishReason`.
- Cerebras: OpenAI-compatible flow; Qwen model family normalized; `ArgumentsJson` preserved.

#### Utilities
- `Serialization/JsonSerialization`: camelCase options and stable stringification.
- `Services/ParameterCorrectionService`: suggests minor key corrections for tool args.

#### Tests & Coverage
- Contract tests for function-calling and raw arguments preservation.
- Streaming tests validate partial deltas and completion signals.
- Provider ListModels tests validate mapping and capabilities.
- Roundtrip tests for multi-call and mixed text+tool scenarios.
- Coverage HTML/Text located in `./TestResults/CoverageReport/`.

#### Current State
- Implementation and tests merged; all tests pass (Azure integration skipped).
- Formatting clean. Providers updated (OpenAI, Azure OpenAI, Cerebras).

#### Next Steps
- Optional: Anthropic adapter alignment for function-calling path.
- Add more provider scenarios (sequencing across multiple turns).
- Document CLI/example retry flow using `ParameterCorrectionService`.
