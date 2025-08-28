#!/bin/bash

# Test script for Ollama integration
echo "=== Testing Ollama Integration ==="
echo ""
echo "This script will test the Ollama example with automatic model detection."
echo ""

# Set Ollama base URL (default to localhost)
export OLLAMA_API_BASE="http://localhost:11434"

# Don't set OLLAMA_MODEL - let the example detect it automatically
unset OLLAMA_MODEL

# Run the Ollama example
echo "Running Ollama example..."
cd examples/Ollama
dotnet run

echo ""
echo "=== Test Complete ==="