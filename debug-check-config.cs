using System;
using Andy.Llm.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

// Check environment variables
Console.WriteLine("Environment Variables:");
Console.WriteLine($"OPENAI_API_KEY: {(Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Length > 0 ? "SET" : "NOT SET")}");
Console.WriteLine($"OPENAI_MODEL: '{Environment.GetEnvironmentVariable("OPENAI_MODEL")}'");
Console.WriteLine($"OPENAI_BASE_URL: '{Environment.GetEnvironmentVariable("OPENAI_BASE_URL")}'");
Console.WriteLine($"OPENAI_API_BASE: '{Environment.GetEnvironmentVariable("OPENAI_API_BASE")}'");
Console.WriteLine();

// Configure from environment
services.ConfigureLlmFromEnvironment();
services.AddLlmServices(options =>
{
    Console.WriteLine($"Default provider: {options.DefaultProvider}");
    Console.WriteLine($"Default model: {options.DefaultModel}");
    foreach (var (name, config) in options.Providers)
    {
        Console.WriteLine($"Provider {name}:");
        Console.WriteLine($"  Model: {config.Model}");
        Console.WriteLine($"  ApiKey: {(config.ApiKey?.Length > 0 ? "SET" : "NOT SET")}");
        Console.WriteLine($"  ApiBase: {config.ApiBase}");
        Console.WriteLine($"  Enabled: {config.Enabled}");
    }
});