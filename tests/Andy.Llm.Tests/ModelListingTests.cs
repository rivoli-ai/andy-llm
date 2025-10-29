using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using System.Linq;
using System.Threading.Tasks;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Andy.Llm.Tests;

public class ModelListingTests
{
    [Fact]
    public async Task OllamaProvider_ListModelsAsync_ReturnsEmptyOnError()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["ollama"] = new ProviderConfig
                {
                    ApiBase = "http://invalid-host:11434",
                    Model = "test-model"
                }
            }
        });

        var logger = new Mock<ILogger<OllamaProvider>>();
        var provider = new OllamaProvider(options, logger.Object);

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task CerebrasProvider_ListModelsAsync_ReturnsEmptyOnError()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "http://invalid-host",
                    Model = "llama-3.3-70b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>();
        var provider = new CerebrasProvider(options, logger.Object);

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        // Since we're now querying the actual API and the test doesn't have a mock endpoint,
        // it should return empty on error
        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task OpenAIProvider_ListModelsAsync_ReturnsEmptyOnError()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "http://invalid-host",
                    Model = "gpt-4"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>();
        var provider = new OpenAIProvider(options, logger.Object);

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        // Since we're now querying the actual API and the test doesn't have a mock endpoint,
        // it should return empty on error
        Assert.NotNull(models);
        Assert.Empty(models);
    }

    [Fact]
    public async Task AzureOpenAIProvider_ListModelsAsync_ReturnsDeploymentInfo()
    {
        // Arrange
        var deploymentName = "gpt-4o-deployment";
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["azure"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://test.openai.azure.com",
                    DeploymentName = deploymentName
                }
            }
        });

        var logger = new Mock<ILogger<AzureOpenAIProvider>>();
        var provider = new AzureOpenAIProvider(options, logger.Object);

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        Assert.NotNull(models);
        var modelList = models.ToList();
        Assert.Single(modelList);

        var model = modelList.First();
        Assert.Equal(deploymentName, model.Id);
        Assert.Equal(deploymentName, model.Name);
        Assert.Equal("azure", model.Provider);
        Assert.Equal("GPT-4o", model.Family);
        Assert.True(model.SupportsFunctions);
        Assert.True(model.SupportsVision);
        Assert.Equal(128000, model.MaxTokens);
    }

    [Fact]
    public void ModelInfo_Properties_CanBeSetAndRetrieved()
    {
        // Arrange & Act
        var model = new ModelInfo
        {
            Id = "test-model",
            Name = "Test Model",
            Provider = "test-provider",
            Description = "A test model",
            // Created = new DateTime(2024, 1, 1), // Read-only in published package
            Family = "TestFamily",
            ParameterSize = "7B",
            MaxTokens = 4096,
            SupportsFunctions = true,
            SupportsVision = false,
            IsFineTuned = false,
            Metadata = new Dictionary<string, object>
            {
                ["custom_field"] = "custom_value"
            }
        };

        // Assert
        Assert.Equal("test-model", model.Id);
        Assert.Equal("Test Model", model.Name);
        Assert.Equal("test-provider", model.Provider);
        Assert.Equal("A test model", model.Description);
        // Created is read-only in the published package, so it will be default/null
        // Assert.Equal(new DateTime(2024, 1, 1), model.Created);
        Assert.Equal("TestFamily", model.Family);
        Assert.Equal("7B", model.ParameterSize);
        Assert.Equal(4096, model.MaxTokens);
        Assert.True(model.SupportsFunctions);
        Assert.False(model.SupportsVision);
        Assert.False(model.IsFineTuned);
        Assert.NotNull(model.Metadata);
        Assert.Equal("custom_value", model.Metadata["custom_field"]);
    }

    [Fact]
    public async Task Provider_ListModelsAsync_HandlesNullMetadata()
    {
        // Arrange
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://api.cerebras.ai/v1",
                    Model = "llama3.1-70b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>();
        var provider = new CerebrasProvider(options, logger.Object);

        // Act
        var models = await provider.ListModelsAsync();

        // Assert
        Assert.NotNull(models);
        foreach (var model in models)
        {
            Assert.NotNull(model.Metadata);
            Assert.NotEmpty(model.Metadata);
        }
    }
}
