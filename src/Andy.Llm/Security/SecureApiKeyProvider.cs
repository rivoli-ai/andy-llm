using System.Security;
using System.Runtime.InteropServices;

namespace Andy.Llm.Security;

/// <summary>
/// Provides secure handling of API keys with in-memory protection.
/// </summary>
public class SecureApiKeyProvider : IDisposable
{
    private readonly Dictionary<string, SecureString> _secureKeys = new();
    private bool _disposed;

    /// <summary>
    /// Stores an API key securely.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <param name="apiKey">The API key to store securely.</param>
    public void SetApiKey(string provider, string apiKey)
    {
        if (string.IsNullOrEmpty(provider))
        {
            throw new ArgumentNullException(nameof(provider));
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey));
        }

        // Remove existing key if present
        if (_secureKeys.TryGetValue(provider, out var existingKey))
        {
            existingKey.Dispose();
        }

        // Create secure string
        var secureKey = new SecureString();
        foreach (char c in apiKey)
        {
            secureKey.AppendChar(c);
        }
        secureKey.MakeReadOnly();

        _secureKeys[provider] = secureKey;
    }

    /// <summary>
    /// Retrieves an API key securely.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <returns>The API key or null if not found.</returns>
    public string? GetApiKey(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            return null;
        }

        if (!_secureKeys.TryGetValue(provider, out var secureKey))
        {
            return null;
        }

        return ConvertToUnsecureString(secureKey);
    }

    /// <summary>
    /// Checks if an API key exists for a provider.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    /// <returns>True if the key exists, false otherwise.</returns>
    public bool HasApiKey(string provider)
    {
        return !string.IsNullOrEmpty(provider) && _secureKeys.ContainsKey(provider);
    }

    /// <summary>
    /// Removes an API key for a provider.
    /// </summary>
    /// <param name="provider">The provider name.</param>
    public void RemoveApiKey(string provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            return;
        }

        if (_secureKeys.TryGetValue(provider, out var secureKey))
        {
            secureKey.Dispose();
            _secureKeys.Remove(provider);
        }
    }

    /// <summary>
    /// Clears all stored API keys.
    /// </summary>
    public void Clear()
    {
        foreach (var secureKey in _secureKeys.Values)
        {
            secureKey.Dispose();
        }
        _secureKeys.Clear();
    }

    private static string ConvertToUnsecureString(SecureString secureString)
    {
        if (secureString == null)
        {
            return string.Empty;
        }

        IntPtr unmanagedString = IntPtr.Zero;
        try
        {
            unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            return Marshal.PtrToStringUni(unmanagedString) ?? string.Empty;
        }
        finally
        {
            if (unmanagedString != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the SecureApiKeyProvider and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Clear();
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
