using Andy.Llm.Security;
using Xunit;

namespace Andy.Llm.Tests;

/// <summary>
/// Tests for security features including API key protection and data sanitization.
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly SecureApiKeyProvider _keyProvider;

    public SecurityTests()
    {
        _keyProvider = new SecureApiKeyProvider();
    }

    [Fact]
    public void SecureApiKeyProvider_SetAndGetApiKey_ShouldWork()
    {
        // Arrange
        const string provider = "openai";
        const string apiKey = "sk-abc123def456ghi789";

        // Act
        _keyProvider.SetApiKey(provider, apiKey);
        var retrievedKey = _keyProvider.GetApiKey(provider);

        // Assert
        Assert.Equal(apiKey, retrievedKey);
    }

    [Fact]
    public void SecureApiKeyProvider_GetNonExistentKey_ShouldReturnNull()
    {
        // Act
        var result = _keyProvider.GetApiKey("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SecureApiKeyProvider_HasApiKey_ShouldReturnCorrectStatus()
    {
        // Arrange
        _keyProvider.SetApiKey("openai", "test-key");

        // Act & Assert
        Assert.True(_keyProvider.HasApiKey("openai"));
        Assert.False(_keyProvider.HasApiKey("nonexistent"));
        Assert.False(_keyProvider.HasApiKey(null!));
    }

    [Fact]
    public void SecureApiKeyProvider_RemoveApiKey_ShouldRemoveKey()
    {
        // Arrange
        _keyProvider.SetApiKey("openai", "test-key");
        Assert.True(_keyProvider.HasApiKey("openai"));

        // Act
        _keyProvider.RemoveApiKey("openai");

        // Assert
        Assert.False(_keyProvider.HasApiKey("openai"));
        Assert.Null(_keyProvider.GetApiKey("openai"));
    }

    [Fact]
    public void SecureApiKeyProvider_Clear_ShouldRemoveAllKeys()
    {
        // Arrange
        _keyProvider.SetApiKey("openai", "key1");
        _keyProvider.SetApiKey("cerebras", "key2");
        _keyProvider.SetApiKey("azure", "key3");

        // Act
        _keyProvider.Clear();

        // Assert
        Assert.False(_keyProvider.HasApiKey("openai"));
        Assert.False(_keyProvider.HasApiKey("cerebras"));
        Assert.False(_keyProvider.HasApiKey("azure"));
    }

    [Fact]
    public void SecureApiKeyProvider_SetApiKey_WithNullProvider_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _keyProvider.SetApiKey(null!, "key"));
    }

    [Fact]
    public void SecureApiKeyProvider_SetApiKey_WithNullKey_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _keyProvider.SetApiKey("provider", null!));
    }

    [Fact]
    public void SensitiveDataSanitizer_Sanitize_ShouldRedactApiKeys()
    {
        // Arrange
        var input = "Using API_KEY=sk-abc123def456 for authentication";

        // Act
        var result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        Assert.Contains("***REDACTED***", result);
        Assert.DoesNotContain("sk-abc123def456", result);
    }

    [Fact]
    public void SensitiveDataSanitizer_Sanitize_ShouldRedactMultiplePatterns()
    {
        // Arrange
        var input = @"
            api_key: sk-test123456789
            bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
            auth_token=mytoken123
            https://user:password@example.com/api
        ";

        // Act
        var result = SensitiveDataSanitizer.Sanitize(input);

        // Assert
        Assert.DoesNotContain("sk-test123456789", result);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.DoesNotContain("mytoken123", result);
        Assert.DoesNotContain("user:password", result);
        Assert.Contains("***REDACTED***", result);
        Assert.Contains("https://***:***@example.com/api", result);
    }

    [Fact]
    public void SensitiveDataSanitizer_Sanitize_WithNull_ShouldReturnEmpty()
    {
        // Act
        var result = SensitiveDataSanitizer.Sanitize(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SensitiveDataSanitizer_MaskApiKey_ShouldMaskCorrectly()
    {
        // Arrange
        var apiKey = "sk-abc123def456ghi789jkl";

        // Act
        var masked = SensitiveDataSanitizer.MaskApiKey(apiKey);

        // Assert
        Assert.StartsWith("sk-a", masked);
        Assert.EndsWith("9jkl", masked);
        Assert.Contains("****", masked);
        Assert.DoesNotContain("123def456", masked);
    }

    [Fact]
    public void SensitiveDataSanitizer_MaskApiKey_ShortKey_ShouldFullyRedact()
    {
        // Arrange
        var shortKey = "abc";

        // Act
        var masked = SensitiveDataSanitizer.MaskApiKey(shortKey);

        // Assert
        Assert.Equal("***REDACTED***", masked);
    }

    [Fact]
    public void SensitiveDataSanitizer_MaskApiKey_WithNull_ShouldReturnEmpty()
    {
        // Act
        var masked = SensitiveDataSanitizer.MaskApiKey(null);

        // Assert
        Assert.Equal("***EMPTY***", masked);
    }

    [Fact]
    public void SensitiveDataSanitizer_ContainsSensitiveData_ShouldDetectPatterns()
    {
        // Assert
        Assert.True(SensitiveDataSanitizer.ContainsSensitiveData("api_key=test123"));
        Assert.True(SensitiveDataSanitizer.ContainsSensitiveData("sk-abc123"));
        Assert.True(SensitiveDataSanitizer.ContainsSensitiveData("bearer token123"));
        Assert.True(SensitiveDataSanitizer.ContainsSensitiveData("https://user:pass@site.com"));
        Assert.False(SensitiveDataSanitizer.ContainsSensitiveData("normal text"));
        Assert.False(SensitiveDataSanitizer.ContainsSensitiveData(null));
    }

    [Fact]
    public void SensitiveDataSanitizer_SanitizeException_ShouldSanitizeMessage()
    {
        // Arrange
        var innerEx = new InvalidOperationException("Inner error with api_key=secret");
        var ex = new ApplicationException("Error with token=abc123", innerEx);

        // Act
        var sanitized = SensitiveDataSanitizer.SanitizeException(ex);

        // Assert
        Assert.Contains("ApplicationException", sanitized);
        Assert.Contains("***REDACTED***", sanitized);
        Assert.DoesNotContain("abc123", sanitized);
        Assert.DoesNotContain("secret", sanitized);
        Assert.Contains("Inner Exception", sanitized);
    }

    [Fact]
    public void SensitiveDataSanitizer_SanitizeException_WithNull_ShouldReturnEmpty()
    {
        // Act
        var result = SensitiveDataSanitizer.SanitizeException(null);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    public void Dispose()
    {
        _keyProvider?.Dispose();
    }
}
