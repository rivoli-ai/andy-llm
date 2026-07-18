using Xunit;

namespace Andy.Llm.Tests;

/// <summary>
/// Unit tests for <see cref="TestConfiguration"/> helpers that gate integration
/// tests. These are pure-string checks and do not touch the environment.
/// </summary>
public class TestConfigurationTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" 1 ")]
    [InlineData("1\n")]
    [InlineData("\ttrue\r\n")]
    public void IsTruthy_AcceptsTrimmedTruthyValues(string value)
    {
        Assert.True(TestConfiguration.IsTruthy(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("yes")]
    [InlineData("2")]
    [InlineData("enabled")]
    public void IsTruthy_RejectsFalsyOrUnrecognizedValues(string? value)
    {
        Assert.False(TestConfiguration.IsTruthy(value));
    }
}
