using Andy.Llm;
using Andy.Llm.Extensions;
using Andy.Llm.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

// Configure from environment variables
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    Console.WriteLine($"Default provider: {options.DefaultProvider}");
    foreach (var (name, config) in options.Providers)
    {
        Console.WriteLine($"Provider {name}: Model={config.Model}, ApiKey={(config.ApiKey?.Length > 0 ? "SET" : "NOT SET")}");
    }
});

var serviceProvider = services.BuildServiceProvider();
var factory = serviceProvider.GetRequiredService<ILlmProviderFactory>();

try
{
    var openai = factory.CreateProvider("openai");
    Console.WriteLine($"OpenAI provider created: {openai.Name}");
}
catch (Exception ex)
{
    Console.WriteLine($"Error creating OpenAI provider: {ex.Message}");
}