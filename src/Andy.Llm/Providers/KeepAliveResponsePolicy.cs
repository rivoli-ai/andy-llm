using System.ClientModel.Primitives;

namespace Andy.Llm.Providers;

/// <summary>Normalizes buffered chat responses before the OpenAI SDK deserializes them.</summary>
internal sealed class KeepAliveResponsePolicy(string provider) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);
        Normalize(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
        Normalize(message);
    }

    private void Normalize(PipelineMessage message)
    {
        // Streaming responses must remain incremental; HTTP errors retain their status and headers.
        if (!message.BufferResponse || message.Response is not { } response || response.Status is < 200 or >= 300)
        {
            return;
        }

        var body = response.Content.ToMemory().IsEmpty ? string.Empty : response.Content.ToString();
        var normalized = KeepAliveResponse.Normalize(body, provider);
        if (normalized != body)
        {
            response.ContentStream = BinaryData.FromString(normalized).ToStream();
            response.BufferContent(message.CancellationToken);
        }
    }
}
