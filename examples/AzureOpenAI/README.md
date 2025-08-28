# Azure OpenAI Service Example

This example demonstrates how to use Azure OpenAI Service with the Andy.Llm library.

## Prerequisites

1. **Azure Subscription**: You need an active Azure subscription
2. **Azure OpenAI Resource**: Deploy an Azure OpenAI resource in your subscription
3. **Model Deployment**: Deploy a model (e.g., gpt-4, gpt-35-turbo) in your resource
4. **API Key**: Get your API key from the Azure portal

## Configuration

### Environment Variables (Recommended)

Set these environment variables before running the example:

```bash
# Required
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com"
export AZURE_OPENAI_KEY="your-api-key-here"
export AZURE_OPENAI_DEPLOYMENT="gpt-4"  # or your deployment name

# Optional
export AZURE_OPENAI_API_VERSION="2024-02-15-preview"  # defaults to this if not set
```

### Configuration File

Alternatively, update the `appsettings.json` file with your settings.

## Running the Example

```bash
dotnet run
```

## What This Example Demonstrates

### 1. Simple Completion
Basic text generation using Azure OpenAI.

### 2. Conversation with Context
Multi-turn conversation with system instructions and message history.

### 3. Streaming Responses
Real-time token streaming for responsive applications.

### 4. Function Calling
OpenAI-compatible function/tool calling (requires compatible deployment).

### 5. Token Usage Tracking
Monitor token consumption and estimate costs.

## Azure OpenAI Benefits

- **Enterprise Security**: Private endpoints, managed identity, VNet integration
- **Compliance**: SOC 2, ISO 27001, HIPAA, and more certifications
- **Content Filtering**: Built-in content moderation and safety systems
- **Regional Deployment**: Deploy in your preferred Azure region
- **Monitoring**: Integration with Azure Monitor and Application Insights
- **SLA**: Enterprise SLA with 99.9% availability
- **Data Privacy**: Your data stays in your subscription, not used for model training

## Deployment Types

Azure OpenAI supports various model deployments:

- **GPT-4**: Most capable model for complex tasks
- **GPT-4 Turbo**: Faster, more cost-effective GPT-4 variant
- **GPT-3.5 Turbo**: Fast and economical for simpler tasks
- **Embeddings Models**: For vector search and similarity
- **DALL-E**: Image generation (separate API)

## Troubleshooting

### "Resource not found" Error
- Verify your endpoint URL format: `https://<resource-name>.openai.azure.com`
- Ensure no trailing slashes in the endpoint URL
- Check that your resource is in a supported region

### "Invalid API Key" Error
- Verify the API key is copied correctly
- Regenerate the key in Azure portal if needed
- Ensure you're using the key from the correct resource

### "Model not found" Error
- Check your deployment name matches exactly
- Verify the deployment is successfully created in Azure portal
- Ensure the deployment is in "Succeeded" state

### Rate Limiting
- Azure OpenAI has rate limits per deployment
- Consider implementing retry logic with exponential backoff
- Monitor your usage in Azure portal

## Pricing

Azure OpenAI pricing is based on:
- **Tokens processed**: Both input (prompt) and output (completion)
- **Model type**: GPT-4 is more expensive than GPT-3.5
- **Fine-tuned models**: Custom models have different pricing

Check the [Azure OpenAI pricing page](https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/) for current rates.

## Security Best Practices

1. **Use Managed Identity** when running in Azure (VMs, App Service, etc.)
2. **Store keys in Azure Key Vault** for production applications
3. **Enable private endpoints** for network isolation
4. **Implement content filtering** for user-facing applications
5. **Monitor usage** with Azure Monitor and alerts
6. **Rotate API keys** regularly

## Additional Resources

- [Azure OpenAI Documentation](https://learn.microsoft.com/azure/ai-services/openai/)
- [Azure OpenAI REST API Reference](https://learn.microsoft.com/azure/ai-services/openai/reference)
- [Azure OpenAI Quotas and Limits](https://learn.microsoft.com/azure/ai-services/openai/quotas-limits)
- [Content Filtering](https://learn.microsoft.com/azure/ai-services/openai/concepts/content-filter)