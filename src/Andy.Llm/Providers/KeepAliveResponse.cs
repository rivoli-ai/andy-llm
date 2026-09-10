using Andy.Llm.Errors;

namespace Andy.Llm.Providers;

/// <summary>Removes the SSE comment preamble used by compatible servers while a non-streaming request runs.</summary>
internal static class KeepAliveResponse
{
    internal static string Normalize(string body, string provider)
    {
        var offset = 0;
        while (offset < body.Length)
        {
            while (offset < body.Length && char.IsWhiteSpace(body[offset]))
            {
                offset++;
            }

            if (offset == body.Length || body[offset] != ':')
            {
                break;
            }

            // Only skip leading comment lines. Never change the JSON payload.
            while (offset < body.Length && body[offset] != '\r' && body[offset] != '\n')
            {
                offset++;
            }
        }

        if (offset == body.Length)
        {
            // A successful HTTP envelope with no completion is an upstream timeout,
            // not malformed JSON. Use the existing 5xx error contract for caller retries.
            throw new LlmProviderException(new LlmProviderError(provider, 504, null,
                "Provider ended the response without a completion (empty or keep-alive-only body). Treat as an upstream timeout and retry."));
        }

        return offset == 0 ? body : body[offset..];
    }
}
