# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Enhanced security features for API key handling
- Retry policies using Polly for improved resilience
- Telemetry and metrics support for observability
- Comprehensive cancellation token support
- Progress reporting for long-running operations
- Timeout configuration options

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

[Unreleased]: https://github.com/rivoli-ai/andy-llm/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/rivoli-ai/andy-llm/releases/tag/v0.1.0