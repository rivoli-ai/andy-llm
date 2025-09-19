using Andy.Model.Llm;
using Andy.Model.Model;
using Andy.Model.Tooling;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Andy.Llm;
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace StructuredOutput;
/// <summary>
/// Example demonstrating structured output capabilities with JSON schemas
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        // Run examples
        var examples = new StructuredOutputExamples(loggerFactory);
        
        Console.WriteLine("=== Structured Output Examples ===\n");
        await examples.BasicJsonSchemaExample();
        await examples.DataExtractionExample();
        await examples.ClassificationExample();
        await examples.MultiStepPlanningExample();
        await examples.ErrorHandlingExample();
    }
}
public class StructuredOutputExamples
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<StructuredOutputExamples> _logger;
    public StructuredOutputExamples(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<StructuredOutputExamples>();
    }
    /// <summary>
    /// Basic example using JSON schema for structured output
    /// </summary>
    public async Task BasicJsonSchemaExample()
    {
        Console.WriteLine("1. Basic JSON Schema Example");
        Console.WriteLine("----------------------------");
        // Define a JSON schema for a product
        var productSchema = @"{
            ""type"": ""object"",
            ""properties"": {
                ""name"": { ""type"": ""string"" },
                ""price"": { ""type"": ""number"", ""minimum"": 0 },
                ""category"": { 
                    ""type"": ""string"", 
                    ""enum"": [""electronics"", ""clothing"", ""food"", ""other""] 
                },
                ""inStock"": { ""type"": ""boolean"" },
                ""tags"": { 
                    ""type"": ""array"", 
                    ""items"": { ""type"": ""string"" } 
                }
            },
            ""required"": [""name"", ""price"", ""category"", ""inStock""],
            ""additionalProperties"": false
        }";
        // Create request with structured output format
        var request = new LlmRequest
        {
            Messages = new List<Message>
            {
                new Message { Role = Role.System, Content = "You are a product data generator. Always respond with valid JSON matching the provided schema." },
                new Message { Role = Role.User, Content = "Generate a sample product for a tech store." }
            },
            Config = new LlmClientConfig
            {
                Model = "gpt-4",
                Temperature = 0.3M
                // TODO: ResponseFormat, JsonSchema, and StrictMode need to be implemented
                // ResponseFormat = ResponseFormat.JsonSchema,
                // JsonSchema = productSchema,
                // StrictMode = true
            }
        };
        // Simulate response (in real usage, this would come from the LLM)
        var response = @"{
            ""name"": ""Wireless Bluetooth Headphones"",
            ""price"": 79.99,
            ""category"": ""electronics"",
            ""inStock"": true,
            ""tags"": [""audio"", ""wireless"", ""bluetooth"", ""portable""]
        }";

        // Parse the structured response
        var parser = new StructuredResponseFactory(_loggerFactory.CreateLogger<StructuredResponseFactory>());
        var structuredResponse = parser.CreateFromGeneric(response, null);
        Console.WriteLine($"Product JSON: {response}");
        // Validate against schema
        var product = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
        Console.WriteLine($"Product Name: {product?["name"]}");
        Console.WriteLine($"Price: ${product?["price"]}");
        Console.WriteLine($"In Stock: {product?["inStock"]}");
        Console.WriteLine();
    }

    /// <summary>
    /// Extract structured data from unstructured text
    /// </summary>
    public async Task DataExtractionExample()
    {
        Console.WriteLine("2. Data Extraction Example");
        Console.WriteLine("--------------------------");
        var extractionSchema = @"{
            ""type"": ""object"",
            ""properties"": {
                ""people"": {
                    ""type"": ""array"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""name"": { ""type"": ""string"" },
                            ""role"": { ""type"": ""string"" },
                            ""email"": { ""type"": ""string"", ""format"": ""email"" }
                        },
                        ""required"": [""name"", ""role""]
                    }
                },
                ""company"": { ""type"": ""string"" },
                ""meetingDate"": { ""type"": ""string"", ""format"": ""date"" },
                ""topics"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""string"" }
                }
            },
            ""required"": [""people"", ""company""]
        }";
        var unstructuredText = @"
            Yesterday I had a meeting with John Smith (CEO, john@techcorp.com) and 
            Sarah Johnson (CTO, sarah@techcorp.com) from TechCorp. We discussed 
            cloud migration strategies, cost optimization, and security best practices.
            The meeting was on 2024-03-15.
        ";
        // Simulate an LLM request with schema
        var messages = new List<Message>
        {
            new Message { Role = Role.System, Content = "Extract structured information from the text according to the schema." },
            new Message { Role = Role.User, Content = unstructuredText }
        };
        // Note: In a real scenario, you would pass the extractionSchema to the LLM
        // Simulated extraction response
        var extractedData = @"{
            ""people"": [
                {
                    ""name"": ""John Smith"",
                    ""role"": ""CEO"",
                    ""email"": ""john@techcorp.com""
                },
                {
                    ""name"": ""Sarah Johnson"",
                    ""role"": ""CTO"",
                    ""email"": ""sarah@techcorp.com""
                }
            ],
            ""company"": ""TechCorp"",
            ""meetingDate"": ""2024-03-15"",
            ""topics"": [
                ""cloud migration strategies"",
                ""cost optimization"",
                ""security best practices""
            ]
        }";

        Console.WriteLine("Original Text:");
        Console.WriteLine(unstructuredText);
        Console.WriteLine("\nExtracted Data:");
        Console.WriteLine(JsonSerializer.Serialize(
            JsonSerializer.Deserialize<object>(extractedData),
            new JsonSerializerOptions { WriteIndented = true }
        ));
    }

    /// <summary>
    /// Classification with confidence scores
    /// </summary>
    public async Task ClassificationExample()
    {
        Console.WriteLine("3. Classification Example");
        Console.WriteLine("------------------------");
        var classificationSchema = @"{
            ""type"": ""object"",
            ""properties"": {
                ""sentiment"": {
                    ""type"": ""string"",
                    ""enum"": [""positive"", ""negative"", ""neutral""]
                },
                ""confidence"": {
                    ""type"": ""number"",
                    ""minimum"": 0,
                    ""maximum"": 1
                },
                ""categories"": {
                    ""type"": ""array"",
                    ""items"": {
                        ""type"": ""string"",
                        ""enum"": [""product"", ""service"", ""pricing"", ""support"", ""delivery""]
                    }
                },
                ""keyPhrases"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""string"" },
                    ""maxItems"": 5
                },
                ""actionRequired"": { ""type"": ""boolean"" },
                ""priority"": {
                    ""type"": ""string"",
                    ""enum"": [""low"", ""medium"", ""high"", ""critical""]
                }
            },
            ""required"": [""sentiment"", ""confidence"", ""categories"", ""actionRequired"", ""priority""]
        }";
        var customerFeedback = "The product quality is excellent, but the delivery was delayed by a week. Customer service was helpful in tracking the order.";

        // Simulate an LLM request with classification schema
        var messages = new List<Message>
        {
            new Message { Role = Role.System, Content = "Classify the customer feedback according to the schema." },
            new Message { Role = Role.User, Content = customerFeedback }
        };
        var classificationResult = @"{
            ""sentiment"": ""neutral"",
            ""confidence"": 0.75,
            ""categories"": [""product"", ""delivery"", ""support""],
            ""keyPhrases"": [
                ""excellent product quality"",
                ""delayed delivery"",
                ""helpful customer service""
            ],
            ""actionRequired"": true,
            ""priority"": ""medium""
        }";

        Console.WriteLine($"Feedback: {customerFeedback}");
        Console.WriteLine("\nClassification Result:");
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(classificationResult);
        Console.WriteLine($"- Sentiment: {result?["sentiment"]} (confidence: {result?["confidence"]})");
        Console.WriteLine($"- Priority: {result?["priority"]}");
        Console.WriteLine($"- Action Required: {result?["actionRequired"]}");
    }

    /// <summary>
    /// Multi-step planning with structured outputs
    /// </summary>
    public async Task MultiStepPlanningExample()
    {
        Console.WriteLine("4. Multi-Step Planning Example");
        Console.WriteLine("------------------------------");
        var planSchema = @"{
            ""type"": ""object"",
            ""properties"": {
                ""goal"": { ""type"": ""string"" },
                ""steps"": {
                    ""type"": ""array"",
                    ""items"": {
                        ""type"": ""object"",
                        ""properties"": {
                            ""stepNumber"": { ""type"": ""integer"", ""minimum"": 1 },
                            ""action"": { ""type"": ""string"" },
                            ""tool"": {
                                ""type"": ""string"",
                                ""enum"": [""search"", ""calculate"", ""write"", ""analyze"", ""none""]
                            },
                            ""parameters"": { ""type"": ""object"" },
                            ""expectedOutput"": { ""type"": ""string"" },
                            ""dependencies"": {
                                ""type"": ""array"",
                                ""items"": { ""type"": ""integer"" }
                            }
                        },
                        ""required"": [""stepNumber"", ""action"", ""tool"", ""expectedOutput""]
                    }
                },
                ""estimatedTime"": { ""type"": ""string"" },
                ""requiredResources"": {
                    ""type"": ""array"",
                    ""items"": { ""type"": ""string"" }
                }
            },
            ""required"": [""goal"", ""steps""]
        }";
        var task = "Create a market analysis report for electric vehicles";
        // Simulate an LLM request with planning schema
        var messages = new List<Message>
        {
            new Message { Role = Role.System, Content = "Create a detailed execution plan matching the schema." },
            new Message { Role = Role.User, Content = task }
        };
        var plan = @"{
            ""goal"": ""Create a comprehensive market analysis report for electric vehicles"",
            ""steps"": [
                {
                    ""stepNumber"": 1,
                    ""action"": ""Research current EV market size and growth trends"",
                    ""tool"": ""search"",
                    ""parameters"": {
                        ""query"": ""electric vehicle market size 2024"",
                        ""sources"": [""industry reports"", ""news""]
                    },
                    ""expectedOutput"": ""Market statistics and growth data"",
                    ""dependencies"": []
                },
                {
                    ""stepNumber"": 2,
                    ""action"": ""Analyze major EV manufacturers and market share"",
                    ""tool"": ""analyze"",
                    ""parameters"": {
                        ""companies"": [""Tesla"", ""BYD"", ""Volkswagen"", ""GM""],
                        ""metrics"": [""market share"", ""sales volume"", ""revenue""]
                    },
                    ""expectedOutput"": ""Competitive landscape analysis"",
                    ""dependencies"": [1]
                },
                {
                    ""stepNumber"": 3,
                    ""action"": ""Calculate market projections for next 5 years"",
                    ""tool"": ""calculate"",
                    ""parameters"": {
                        ""growthRate"": 0.25,
                        ""timeframe"": ""5 years""
                    },
                    ""expectedOutput"": ""Future market projections"",
                    ""dependencies"": [1, 2]
                },
                {
                    ""stepNumber"": 4,
                    ""action"": ""Write comprehensive market analysis report"",
                    ""tool"": ""write"",
                    ""parameters"": {
                        ""sections"": [""Executive Summary"", ""Market Overview"", ""Competition"", ""Projections""],
                        ""format"": ""PDF""
                    },
                    ""expectedOutput"": ""Complete market analysis report"",
                    ""dependencies"": [1, 2, 3]
                }
            ],
            ""estimatedTime"": ""4-6 hours"",
            ""requiredResources"": [""Market research databases"", ""Industry reports"", ""Analysis tools""]
        }";

        Console.WriteLine($"Task: {task}");
        Console.WriteLine("\nGenerated Plan:");
        var planData = JsonSerializer.Deserialize<Dictionary<string, object>>(plan);
        var steps = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(planData?["steps"]?.ToString() ?? "[]");
        foreach (var step in steps ?? new List<Dictionary<string, object>>())
        {
            Console.WriteLine($"Step {step["stepNumber"]}: {step["action"]}");
            Console.WriteLine($"  Tool: {step["tool"]}");
            Console.WriteLine($"  Output: {step["expectedOutput"]}");
        }
    }

    /// <summary>
    /// Error handling and validation example
    /// </summary>
    public async Task ErrorHandlingExample()
    {
        Console.WriteLine("5. Error Handling Example");
        // Create a structured response factory for parsing
        var structuredFactory = new StructuredResponseFactory(
            _loggerFactory.CreateLogger<StructuredResponseFactory>()
        );
        // Example of malformed tool call response
        var malformedResponse = @"{
            ""tool_calls"": [
                {
                    ""id"": ""call_123"",
                    ""function"": {
                        ""name"": ""calculate"",
                        ""arguments"": ""{ invalid json: true }""
                    }
                }
            ]
        }";

        Console.WriteLine("Parsing malformed response with error capture:");
        var response = structuredFactory.CreateFromOpenAI(malformedResponse);
        foreach (var toolCall in response.ToolCalls)
        {
            Console.WriteLine($"Tool: {toolCall.Name}");
            Console.WriteLine($"Call ID: {toolCall.Id}");
            
            if (toolCall.ParseError != null)
            {
                Console.WriteLine($"⚠️ Parse Error: {toolCall.ParseError.Message}");
                Console.WriteLine($"Raw Arguments: {toolCall.ArgumentsJson}");
            }
            else
            {
                Console.WriteLine($"✅ Arguments parsed successfully");
            }
        }

        // Example of validation with schema
        Console.WriteLine("\nValidation Example:");
        var invalidData = @"{
            ""name"": ""Product"",
            ""price"": -50,
            ""category"": ""invalid_category""
        }";
        Console.WriteLine("Invalid product data:");
        Console.WriteLine(invalidData);
        Console.WriteLine("Validation errors would be captured in the AST as ErrorNodes");
    }
}
