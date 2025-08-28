# Ollama Local LLM Example

This example demonstrates how to use Ollama for running Large Language Models locally with the Andy.Llm library.

## Prerequisites

### 1. Install Ollama

Download and install Ollama from [https://ollama.ai](https://ollama.ai)

- **macOS**: `brew install ollama` or download from website
- **Linux**: `curl -fsSL https://ollama.ai/install.sh | sh`
- **Windows**: Download installer from website

### 2. Pull a Model

Download at least one model to use:

```bash
# Popular models
ollama pull llama2          # Meta's Llama 2 (default)
ollama pull mistral         # Mistral 7B
ollama pull codellama       # Code-specialized Llama
ollama pull phi             # Microsoft's Phi-2
ollama pull neural-chat     # Intel's Neural Chat
ollama pull starling-lm     # Berkeley's Starling

# Larger models (require more RAM)
ollama pull llama2:13b      # 13B parameter version
ollama pull llama2:70b      # 70B parameter version (requires 32GB+ RAM)
```

### 3. Start Ollama Server

```bash
ollama serve
```

The server will start on `http://localhost:11434` by default.

## Configuration

### Environment Variables

```bash
# Optional - defaults shown
export OLLAMA_API_BASE="http://localhost:11434"
export OLLAMA_MODEL="llama2"
```

### Custom Port/Host

To run Ollama on a different port or host:

```bash
OLLAMA_HOST=0.0.0.0:8080 ollama serve
```

Then update your configuration:

```bash
export OLLAMA_API_BASE="http://localhost:8080"
```

## Running the Example

```bash
dotnet run
```

## What This Example Demonstrates

### 1. Simple Completion
Basic text generation using local models.

### 2. Conversation with Context
Multi-turn conversations with message history.

### 3. Streaming Responses
Real-time token streaming from local models.

### 4. Code Generation
Using models for code generation tasks.

### 5. Performance Metrics
Measuring inference speed and token generation rates.

### 6. Model Comparison
Comparing responses from different models (if multiple installed).

## Available Models

### Small Models (4-8GB RAM)
- `llama2` - General purpose, well-rounded
- `mistral` - Fast, efficient, good quality
- `phi` - Microsoft's compact model
- `orca-mini` - Small but capable

### Medium Models (8-16GB RAM)
- `llama2:13b` - Larger Llama 2
- `codellama:13b` - Code-focused model
- `vicuna:13b` - Fine-tuned for conversations

### Large Models (32GB+ RAM)
- `llama2:70b` - Largest Llama 2
- `mixtral` - Mixture of experts model

### Specialized Models
- `codellama` - Optimized for code
- `sqlcoder` - SQL query generation
- `medllama2` - Medical domain
- `stable-beluga` - Instruction following

## Performance Tips

### Hardware Requirements

| Model Size | Minimum RAM | Recommended RAM | GPU Support |
|------------|-------------|-----------------|-------------|
| 7B         | 8GB         | 16GB            | Optional    |
| 13B        | 16GB        | 32GB            | Recommended |
| 70B        | 32GB        | 64GB            | Required    |

### Optimization Strategies

1. **Use Smaller Models**: Start with 7B models for faster inference
2. **Quantization**: Ollama uses quantized models by default (4-bit)
3. **GPU Acceleration**: Enable GPU support for faster inference
4. **Batch Processing**: Process multiple requests together
5. **Context Length**: Limit context size for better performance

### GPU Acceleration

Ollama automatically uses GPU if available:

- **NVIDIA**: Requires CUDA 11.7+
- **AMD**: ROCm support on Linux
- **Apple Silicon**: Metal support on macOS

Check GPU usage:
```bash
ollama ps  # Shows running models and resource usage
```

## Troubleshooting

### "Connection refused" Error
- Ensure Ollama is running: `ollama serve`
- Check the port: default is 11434
- Verify firewall settings

### "Model not found" Error
- Pull the model first: `ollama pull llama2`
- List available models: `ollama list`
- Check model name spelling

### Slow Performance
- Use smaller models (7B instead of 13B/70B)
- Enable GPU acceleration if available
- Reduce max tokens and context size
- Close other memory-intensive applications

### Out of Memory
- Use smaller models
- Reduce context window size
- Enable swap space (Linux/macOS)
- Use quantized versions (default)

## Model Management

### List Models
```bash
ollama list
```

### Model Information
```bash
ollama show llama2
```

### Remove Models
```bash
ollama rm llama2:13b
```

### Update Models
```bash
ollama pull llama2  # Re-pulls latest version
```

## Advanced Usage

### Custom Models

Create custom models with Modelfile:

```dockerfile
# Modelfile
FROM llama2
PARAMETER temperature 0.7
PARAMETER top_p 0.9
SYSTEM "You are a helpful assistant specialized in C# and .NET development."
```

Build and use:
```bash
ollama create dotnet-assistant -f Modelfile
export OLLAMA_MODEL="dotnet-assistant"
```

### API Endpoints

Ollama exposes several endpoints:

- `/api/generate` - Text generation
- `/api/chat` - Chat completions (used by Andy.Llm)
- `/api/embeddings` - Generate embeddings
- `/api/tags` - List available models
- `/api/show` - Model information
- `/api/pull` - Download models
- `/api/push` - Upload custom models

## Security Considerations

1. **Local Network**: By default, Ollama only listens on localhost
2. **No Authentication**: Add a reverse proxy for authentication if needed
3. **Resource Limits**: Set memory/CPU limits in production
4. **Model Sources**: Only pull models from trusted sources
5. **Data Privacy**: All processing is local - no data leaves your machine

## Benefits of Local LLMs

✅ **Complete Privacy**: Data never leaves your machine
✅ **No API Costs**: One-time download, unlimited use
✅ **Offline Operation**: Works without internet
✅ **Low Latency**: No network round trips
✅ **Customization**: Fine-tune or create custom models
✅ **Control**: Full control over model and infrastructure

## Limitations

❌ **Hardware Requirements**: Needs significant RAM/GPU
❌ **Model Quality**: Generally lower than GPT-4 class models
❌ **Model Size**: Large models require substantial resources
❌ **Update Frequency**: Manual model updates required
❌ **Feature Set**: No built-in function calling (yet)

## Additional Resources

- [Ollama Documentation](https://github.com/ollama/ollama)
- [Ollama Model Library](https://ollama.ai/library)
- [Ollama Discord Community](https://discord.gg/ollama)
- [Model Benchmarks](https://github.com/ollama/ollama#benchmarks)