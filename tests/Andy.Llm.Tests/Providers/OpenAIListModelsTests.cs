using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Andy.Llm.Configuration;
using Andy.Llm.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Andy.Llm.Tests.Providers;

public class OpenAIListModelsTests
{
    [Fact]
    public async Task ListModelsAsync_ShouldMapFields_AndSupportsFunctions()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["openai"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://fake.openai.local/v1/",
                    Model = "gpt-4o-mini"
                }
            }
        });

        var logger = new Mock<ILogger<OpenAIProvider>>().Object;

        // Mock HTTP handler for /models
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.AbsolutePath.Contains("models")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var payload = new
                {
                    data = new[]
                    {
                        new { id = "gpt-4o", created = 1720000000, owned_by = "openai" },
                        new { id = "gpt-3.5-turbo", created = 1710000000, owned_by = "openai" }
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://fake.openai.local/v1/")
        };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var provider = new OpenAIProvider(options, logger, factory.Object);

        var models = (await provider.ListModelsAsync()).ToList();
        Assert.True(models.Count >= 2);

        var gpt4o = models.First(m => m.Id == "gpt-4o");
        Assert.Equal("GPT-4o", gpt4o.Family);
        Assert.True(gpt4o.SupportsFunctions);
        Assert.Equal("openai", gpt4o.Provider);

        var gpt35 = models.First(m => m.Id == "gpt-3.5-turbo");
        Assert.Equal("GPT-3.5", gpt35.Family);
        Assert.True(gpt35.SupportsFunctions);
    }
}
