using System;
using Andy.Llm.Llm;using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Andy.Llm.Parsing;
using Andy.Llm.Parsing.Ast;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HybridParsing;

/// <summary>
/// Example demonstrating hybrid parsing that handles both structured and text responses
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        // Setup DI container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var examples = new HybridParsingExamples(loggerFactory);
        
        Console.WriteLine("=== Hybrid Parsing Examples ===\n");
        
        await examples.ParseOpenAIResponse();
        await examples.ParseAnthropicResponse();
        await examples.ParseTextWithEmbeddedTools();
        await examples.ParseMixedContent();
        await examples.StreamingExample();
        await examples.ASTTraversalExample();
    }
}

public class HybridParsingExamples
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly HybridLlmParser _hybridParser;

    public HybridParsingExamples(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        
        // Create the hybrid parser with a text parser fallback
        var textParser = new SimpleTextParser(); // Your text parser implementation
        var structuredFactory = new StructuredResponseFactory(
            loggerFactory.CreateLogger<StructuredResponseFactory>()
        );
        
        _hybridParser = new HybridLlmParser(
            textParser,
            structuredFactory,
            loggerFactory.CreateLogger<HybridLlmParser>()
        );
    }

    /// <summary>
    /// Parse an OpenAI-style structured response
    /// </summary>
    public async Task ParseOpenAIResponse()
    {
        Console.WriteLine("1. OpenAI Structured Response");
        Console.WriteLine("-----------------------------");

        var openAIResponse = @"{
            ""id"": ""chatcmpl-123"",
            ""object"": ""chat.completion"",
            ""created"": 1677652288,
            ""model"": ""gpt-4-turbo"",
            ""choices"": [{
                ""index"": 0,
                ""message"": {
                    ""role"": ""assistant"",
                    ""content"": ""I'll help you search for that information and perform the calculation."",
                    ""tool_calls"": [
                        {
                            ""id"": ""call_abc123"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""web_search"",
                                ""arguments"": ""{\""query\"": \""latest AI breakthroughs 2024\"", \""limit\"": 5}""
                            }
                        },
                        {
                            ""id"": ""call_def456"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""calculate"",
                                ""arguments"": ""{\""expression\"": \""42 * 3.14159\""}""
                            }
                        }
                    ]
                },
                ""finish_reason"": ""tool_calls""
            }],
            ""usage"": {
                ""prompt_tokens"": 50,
                ""completion_tokens"": 100,
                ""total_tokens"": 150
            }
        }";

        var ast = _hybridParser.Parse(openAIResponse);
        
        Console.WriteLine($"Provider: {ast.ModelProvider}");
        Console.WriteLine($"Model: {ast.ModelName}");
        Console.WriteLine($"Total Tokens: {ast.ResponseMetadata.TokenCount}");
        Console.WriteLine("\nContent:");
        
        PrintAST(ast);
        Console.WriteLine();
    }

    /// <summary>
    /// Parse an Anthropic-style response with content blocks
    /// </summary>
    public async Task ParseAnthropicResponse()
    {
        Console.WriteLine("2. Anthropic Structured Response");
        Console.WriteLine("--------------------------------");

        var anthropicResponse = @"{
            ""id"": ""msg_123"",
            ""type"": ""message"",
            ""role"": ""assistant"",
            ""content"": [
                {
                    ""type"": ""text"",
                    ""text"": ""I'll analyze this data for you using multiple tools.""
                },
                {
                    ""type"": ""tool_use"",
                    ""id"": ""toolu_abc"",
                    ""name"": ""data_analyzer"",
                    ""input"": {
                        ""dataset"": ""sales_2024"",
                        ""metrics"": [""revenue"", ""growth""],
                        ""groupBy"": ""quarter""
                    }
                },
                {
                    ""type"": ""text"",
                    ""text"": ""Now let me visualize the results:""
                },
                {
                    ""type"": ""tool_use"",
                    ""id"": ""toolu_def"",
                    ""name"": ""create_chart"",
                    ""input"": {
                        ""type"": ""line"",
                        ""title"": ""Quarterly Revenue Growth""
                    }
                }
            ],
            ""model"": ""claude-3-opus"",
            ""stop_reason"": ""tool_use"",
            ""usage"": {
                ""input_tokens"": 120,
                ""output_tokens"": 85
            }
        }";

        var ast = _hybridParser.Parse(anthropicResponse);
        
        Console.WriteLine($"Provider: {ast.ModelProvider}");
        Console.WriteLine($"Finish Reason: {ast.ResponseMetadata.FinishReason}");
        Console.WriteLine("\nContent Flow:");
        
        foreach (var node in ast.Children)
        {
            switch (node)
            {
                case TextNode text:
                    Console.WriteLine($"💬 Text: {text.Content}");
                    break;
                case ToolCallNode tool:
                    Console.WriteLine($"🔧 Tool Call: {tool.ToolName} (ID: {tool.CallId})");
                    if (tool.Arguments != null)
                    {
                        foreach (var arg in tool.Arguments)
                        {
                            Console.WriteLine($"   - {arg.Key}: {arg.Value}");
                        }
                    }
                    break;
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Parse text response with embedded tool calls (Qwen-style)
    /// </summary>
    public async Task ParseTextWithEmbeddedTools()
    {
        Console.WriteLine("3. Text with Embedded Tool Calls");
        Console.WriteLine("--------------------------------");

        var textResponse = @"Let me help you with that calculation. First, I'll search for the current exchange rate.

{""tool_call"": {""name"": ""get_exchange_rate"", ""arguments"": {""from"": ""USD"", ""to"": ""EUR""}}}

Based on the current rate, I'll now convert your amount.

{""tool_call"": {""name"": ""convert_currency"", ""arguments"": {""amount"": 1000, ""from"": ""USD"", ""to"": ""EUR"", ""rate"": 0.92}}}

The conversion shows that $1000 USD equals approximately €920 EUR at today's exchange rate.";

        var ast = _hybridParser.Parse(textResponse);
        
        Console.WriteLine("Parsed Content Structure:");
        int elementCount = 1;
        
        foreach (var node in ast.Children)
        {
            Console.WriteLine($"{elementCount++}. {node.NodeType}: {GetNodeSummary(node)}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Parse mixed content with various node types
    /// </summary>
    public async Task ParseMixedContent()
    {
        Console.WriteLine("4. Mixed Content Parsing");
        Console.WriteLine("-----------------------");

        var mixedResponse = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""Here's the code solution:\n\n```python\ndef fibonacci(n):\n    if n <= 1:\n        return n\n    return fibonacci(n-1) + fibonacci(n-2)\n```\n\nNow let me test it:"",
                    ""tool_calls"": [{
                        ""id"": ""call_789"",
                        ""type"": ""function"",
                        ""function"": {
                            ""name"": ""execute_code"",
                            ""arguments"": ""{\""language\"": \""python\"", \""code\"": \""print(fibonacci(10))\""}""
                        }
                    }]
                }
            }],
            ""model"": ""gpt-4""
        }";

        var ast = _hybridParser.Parse(mixedResponse);
        
        Console.WriteLine("Content Analysis:");
        
        // Count different node types
        var nodeTypes = ast.Children.GroupBy(n => n.NodeType)
            .Select(g => new { Type = g.Key, Count = g.Count() });
        
        foreach (var nodeType in nodeTypes)
        {
            Console.WriteLine($"- {nodeType.Type}: {nodeType.Count}");
        }
        
        // Show tool calls
        var toolCalls = ast.Children.OfType<ToolCallNode>();
        foreach (var tool in toolCalls)
        {
            Console.WriteLine($"\nTool: {tool.ToolName}");
            if (tool.ParseError != null)
            {
                Console.WriteLine($"⚠️ Parse Error: {tool.ParseError.Message}");
            }
            else
            {
                Console.WriteLine($"✅ Arguments parsed successfully");
            }
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Streaming response parsing example
    /// </summary>
    public async Task StreamingExample()
    {
        Console.WriteLine("5. Streaming Response Parsing");
        Console.WriteLine("----------------------------");

        // Simulate streaming chunks
        var chunks = new List<string>
        {
            "{\"choices\":[{\"delta\":{",
            "\"content\":\"I'll search",
            " for that",
            " information.\"},",
            "\"tool_calls\":[{\"id\":\"",
            "call_123\",\"function\":",
            "{\"name\":\"search\",",
            "\"arguments\":\"{\\\"query\\\":",
            "\\\"test\\\"}\"}}]}]}"
        };

        Console.WriteLine("Streaming chunks:");
        foreach (var chunk in chunks)
        {
            Console.WriteLine($"  Chunk: {chunk}");
        }

        var ast = await _hybridParser.ParseStreamingAsync(ToAsyncEnumerable(chunks));
        
        Console.WriteLine("\nParsed Result:");
        Console.WriteLine($"- Complete: {ast.ResponseMetadata.IsComplete}");
        Console.WriteLine($"- Nodes: {ast.Children.Count}");
        
        foreach (var node in ast.Children)
        {
            Console.WriteLine($"  - {node.NodeType}");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// AST traversal and visitor pattern example
    /// </summary>
    public async Task ASTTraversalExample()
    {
        Console.WriteLine("6. AST Traversal Example");
        Console.WriteLine("-----------------------");

        var response = @"{
            ""choices"": [{
                ""message"": {
                    ""content"": ""Analyzing your request..."",
                    ""tool_calls"": [
                        {
                            ""id"": ""call_1"",
                            ""type"": ""function"",
                            ""function"": {
                                ""name"": ""analyze_sentiment"",
                                ""arguments"": ""{\""text\"": \""Great product!\"", \""language\"": \""en\""}""
                            }
                        }
                    ]
                }
            }]
        }";

        var ast = _hybridParser.Parse(response);
        
        // Create a custom visitor to extract specific information
        var visitor = new ToolCallExtractor();
        var rootInfo = ast.Accept(visitor);
        
        Console.WriteLine("AST Visitor Results:");
        Console.WriteLine($"- Root node processed: {rootInfo}");
        Console.WriteLine($"- Tool calls found: {visitor.ToolCalls.Count}");
        
        foreach (var tool in visitor.ToolCalls)
        {
            Console.WriteLine($"  - {tool.ToolName}: {tool.CallId}");
        }
        
        // Validate the AST
        var validation = _hybridParser.Validate(ast);
        Console.WriteLine($"\nValidation: {(validation.IsValid ? "✅ Valid" : "❌ Invalid")}");
        
        if (!validation.IsValid)
        {
            foreach (var issue in validation.Issues)
            {
                Console.WriteLine($"  - {issue.Severity}: {issue.Message}");
            }
        }
        Console.WriteLine();
    }

    // Helper methods
    private void PrintAST(AstNode node, int indent = 0)
    {
        var indentStr = new string(' ', indent * 2);
        
        switch (node)
        {
            case ResponseNode response:
                foreach (var child in response.Children)
                {
                    PrintAST(child, indent);
                }
                break;
                
            case TextNode text:
                Console.WriteLine($"{indentStr}📝 Text: {text.Content.Substring(0, Math.Min(50, text.Content.Length))}...");
                break;
                
            case ToolCallNode tool:
                Console.WriteLine($"{indentStr}🔧 Tool: {tool.ToolName} (ID: {tool.CallId})");
                if (tool.Arguments != null && tool.Arguments.Any())
                {
                    Console.WriteLine($"{indentStr}   Args: {string.Join(", ", tool.Arguments.Keys)}");
                }
                break;
                
            case ToolResultNode result:
                Console.WriteLine($"{indentStr}✅ Result: {result.ToolName} - {(result.IsSuccess ? "Success" : "Failed")}");
                break;
                
            case ErrorNode error:
                Console.WriteLine($"{indentStr}❌ Error: {error.Message}");
                break;
                
            default:
                Console.WriteLine($"{indentStr}📦 {node.NodeType}");
                break;
        }
        
        foreach (var child in node.Children)
        {
            PrintAST(child, indent + 1);
        }
    }

    private string GetNodeSummary(AstNode node)
    {
        return node switch
        {
            TextNode text => text.Content.Length > 50 
                ? text.Content.Substring(0, 47) + "..." 
                : text.Content,
            ToolCallNode tool => $"{tool.ToolName}({tool.Arguments?.Count ?? 0} args)",
            ErrorNode error => error.Message,
            _ => node.NodeType
        };
    }

    private async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Delay(10); // Simulate streaming delay
        }
    }
}

/// <summary>
/// Custom visitor implementation for extracting tool calls
/// </summary>
public class ToolCallExtractor : IAstVisitor<string>
{
    public List<ToolCallNode> ToolCalls { get; } = new();

    public string VisitResponse(ResponseNode node)
    {
        foreach (var child in node.Children)
        {
            child.Accept(this);
        }
        return "ResponseNode";
    }

    public string VisitText(TextNode node) => "TextNode";
    
    public string VisitToolCall(ToolCallNode node)
    {
        ToolCalls.Add(node);
        return $"ToolCall:{node.ToolName}";
    }

    public string VisitToolResult(ToolResultNode node) => "ToolResultNode";
    public string VisitCode(CodeNode node) => "CodeNode";
    public string VisitFileReference(FileReferenceNode node) => "FileReferenceNode";
    public string VisitQuestion(QuestionNode node) => "QuestionNode";
    public string VisitThought(ThoughtNode node) => "ThoughtNode";
    public string VisitMarkdown(MarkdownNode node) => "MarkdownNode";
    public string VisitError(ErrorNode node) => "ErrorNode";
    public string VisitCommand(CommandNode node) => "CommandNode";
}

/// <summary>
/// Simple text parser for fallback (simplified implementation)
/// </summary>
public class SimpleTextParser : ILlmResponseParser
{
    public ResponseNode Parse(string input, ParserContext? context = null)
    {
        var response = new ResponseNode
        {
            ModelProvider = "text",
            Timestamp = DateTime.UtcNow
        };

        // Simple parsing - just add as text
        // In a real implementation, this would parse embedded JSON, code blocks, etc.
        if (!string.IsNullOrWhiteSpace(input))
        {
            response.Children.Add(new TextNode
            {
                Content = input,
                Format = TextFormat.Plain
            });
        }

        return response;
    }

    public async Task<ResponseNode> ParseStreamingAsync(
        IAsyncEnumerable<string> chunks,
        ParserContext? context = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var buffer = new System.Text.StringBuilder();
        
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            buffer.Append(chunk);
        }
        
        return Parse(buffer.ToString(), context);
    }

    public ValidationResult Validate(ResponseNode ast)
    {
        return ValidationResult.Success();
    }

    public ParserCapabilities GetCapabilities()
    {
        return new ParserCapabilities
        {
            SupportsStreaming = true,
            SupportsToolCalls = false,
            SupportsCodeBlocks = true,
            SupportsMarkdown = true,
            SupportedFormats = new List<string> { "text", "plain" }
        };
    }
}