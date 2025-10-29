using Xunit;

namespace Andy.Llm.Tests;

/// <summary>
/// Collection definition for tests that modify environment variables.
/// Tests in this collection will not run in parallel with each other.
/// </summary>
[CollectionDefinition("EnvironmentVariable Tests")]
public class EnvironmentVariableTestCollection
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
