using System.Net;
using System.Net.Http;
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

public class CerebrasListModelsTests
{
    [Fact]
    public async Task ListModelsAsync_ShouldMapFields_AndNormalizeFamilies()
    {
        var options = Options.Create(new LlmOptions
        {
            Providers = new Dictionary<string, ProviderConfig>
            {
                ["cerebras"] = new ProviderConfig
                {
                    ApiKey = "test-key",
                    ApiBase = "https://fake.cerebras.local/v1/",
                    Model = "llama-3.3-70b"
                }
            }
        });

        var logger = new Mock<ILogger<CerebrasProvider>>().Object;

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
                        new { id = "llama-3.3-70b", created = 1720000000, owned_by = "cerebras" },
                        new { id = "qwen-2.5-7b", created = 1720000001, owned_by = "cerebras" }
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
            });

        var httpClient = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("https://fake.cerebras.local/v1/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var provider = new CerebrasProvider(options, logger, factory.Object);

        var models = (await provider.ListModelsAsync()).ToList();
        Assert.True(models.Count >= 2);

        var llama = models.First(m => m.Id == "llama-3.3-70b");
        Assert.Equal("Llama", llama.Family);
        Assert.True(llama.SupportsFunctions);

        var qwen = models.First(m => m.Id == "qwen-2.5-7b");
        Assert.Equal("Qwen", qwen.Family);
        Assert.False(qwen.SupportsFunctions);
    }
}
