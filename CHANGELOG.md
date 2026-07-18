# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.9] - 2026-07-17

### Added
- OpenRouter provider with support for provider routing, fallbacks, and
  request-level `ExtraBody` options emitted on `LlmRequest`
- Anthropic provider, including prompt-caching `cache_control` breakpoints
- Gateway provider for routing requests through andy-models
- Multi-endpoint provider architecture with a strategy pattern
- Azure OpenAI provider implementation with deployment-based model access
- Ollama provider implementation for local LLM execution
- ZAI GLM 4.6 model support in the Cerebras provider with function calling
- Enhanced security features for API key handling
- Retry policies using Polly for improved resilience
- Telemetry and metrics support for observability
- Comprehensive cancellation token support
- Progress reporting for long-running operations
- Timeout configuration options
- HttpClientFactory support for better HTTP client management
- Extended configuration options for Azure and Ollama providers
- Dedicated Azure OpenAI and Ollama examples
- OpenRouter example
- Comprehensive tests for Azure, Ollama, and Cerebras function calling

### Changed
- Updated Andy.Model dependency to the published 2026.6.20-rc.10 release
- Providers now throw on provider errors instead of synthesising placeholder
  `LlmResponse` values
- Malformed tool-call arguments are repaired at every provider boundary

### Fixed
- OpenRouter provider streaming crash on an empty choices array
- OpenRouter provider guarded against null tool collections when converting
  messages
- Duplicate-README pack warning during packaging

### Documentation
- Updated Getting Started guide with all supported providers
- Enhanced API Reference with Azure and Ollama provider details
- Updated Architecture documentation with current provider implementations
- Added detailed README files for Azure and Ollama examples

## [0.1.0] - 2024-08-28

### Added
- Initial release of Andy.Llm library
- Multi-provider support (OpenAI, Cerebras, Azure OpenAI, Ollama)
- Streaming response capabilities
- Function/tool calling support
- Conversation context management
- Dependency injection integration
- Environment variable configuration
- Comprehensive test suite
- Documentation (README, Architecture, API Reference, Getting Started)

### Features
- Provider-agnostic design with unified interface
- OpenAI-compatible API support
- Automatic provider fallback mechanism
- Token limit management in conversations
- Type-safe models and interfaces
- Modern .NET 8.0 implementation

[Unreleased]: https://github.com/rivoli-ai/andy-llm/compare/v0.1.9...HEAD
[0.1.9]: https://github.com/rivoli-ai/andy-llm/compare/v0.1.0...v0.1.9
[0.1.0]: https://github.com/rivoli-ai/andy-llm/releases/tag/v0.1.0