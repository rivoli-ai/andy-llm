using System.Collections.Generic;
using System.Text.Json;
using Andy.Llm.Parsing;
using Xunit;

namespace Andy.Llm.Tests.Parsing;

/// <summary>
/// Comprehensive, realistic tests for repairing the malformed tool-call argument JSON that
/// models actually emit: markdown fences, surrounding prose, trailing commas, single quotes,
/// unquoted keys, Python literals, smart quotes, and double-encoded strings.
/// </summary>
public class ToolArgumentJsonRepairTests
{
    // ---- Cases that should repair to a single string-valued property ----
    public static IEnumerable<object[]> RepairableStringCases() => new[]
    {
        // description, raw input, key, expected string value
        new object[] { "valid json passthrough", "{\"file_path\": \"src/app.py\"}", "file_path", "src/app.py" },
        new object[] { "json markdown fence", "```json\n{\"path\": \"x.py\"}\n```", "path", "x.py" },
        new object[] { "bare markdown fence", "```\n{\"path\": \"x.py\"}\n```", "path", "x.py" },
        new object[] { "leading prose", "Sure, here are the arguments: {\"query\": \"def foo\"}", "query", "def foo" },
        new object[] { "trailing prose", "{\"query\": \"def foo\"} — let me know if that helps!", "query", "def foo" },
        new object[] { "single-quoted dict", "{'path': 'README.md'}", "path", "README.md" },
        new object[] { "unquoted key", "{path: \"README.md\"}", "path", "README.md" },
        new object[] { "smart quotes", "{“path”: “README.md”}", "path", "README.md" },
        new object[] { "double-encoded string", "\"{\\\"path\\\": \\\"x.py\\\"}\"", "path", "x.py" },
        new object[] { "fence + single quotes + trailing comma",
            "```json\n{'path': 'a.py', 'mode': 'rewrite',}\n```", "mode", "rewrite" },
        new object[] { "windows path with backslashes", "{\"file_path\": \"C:\\\\repo\\\\a.py\"}", "file_path", "C:\\repo\\a.py" },
    };

    [Theory]
    [MemberData(nameof(RepairableStringCases))]
    public void Repairs_To_Expected_String_Value(string description, string raw, string key, string expected)
    {
        Assert.True(ToolArgumentJsonRepair.TryRepair(raw, out var json), $"should repair: {description}");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal(expected, doc.RootElement.GetProperty(key).GetString());

        // ParseArguments should agree.
        var args = ToolArgumentJsonRepair.ParseArguments(raw);
        Assert.Equal(expected, Assert.IsType<string>(args[key]));
    }

    [Fact]
    public void Removes_Trailing_Commas_In_Object_And_Array()
    {
        var args = ToolArgumentJsonRepair.ParseArguments("{\"items\": [1, 2, 3,], \"name\": \"x\",}");
        Assert.Equal("x", args["name"]);
        Assert.Contains("\"items\"", ToolArgumentJsonRepair.TryRepair("{\"items\": [1,2,3,],}", out var j) ? j : "");
    }

    [Fact]
    public void Normalizes_Python_Literals()
    {
        var args = ToolArgumentJsonRepair.ParseArguments("{\"recursive\": True, \"force\": False, \"limit\": None}");
        Assert.Equal(true, args["recursive"]);
        Assert.Equal(false, args["force"]);
        Assert.Null(args["limit"]);
    }

    [Fact]
    public void Preserves_Numbers_Booleans_Null_Types()
    {
        var args = ToolArgumentJsonRepair.ParseArguments("{\"n\": 5, \"f\": 1.5, \"b\": false, \"x\": null}");
        Assert.Equal(5L, args["n"]);
        Assert.Equal(1.5d, args["f"]);
        Assert.Equal(false, args["b"]);
        Assert.Null(args["x"]);
    }

    [Fact]
    public void Preserves_Nested_Object_And_Array()
    {
        var raw = "{\"opts\": {\"deep\": [1, 2, 3]}, \"name\": \"x\",}"; // trailing comma to force repair
        Assert.True(ToolArgumentJsonRepair.TryRepair(raw, out var json));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("x", doc.RootElement.GetProperty("name").GetString());
        var deep = doc.RootElement.GetProperty("opts").GetProperty("deep");
        Assert.Equal(JsonValueKind.Array, deep.ValueKind);
        Assert.Equal(3, deep.GetArrayLength());
    }

    [Fact]
    public void Valid_Json_Is_Returned_Unchanged()
    {
        const string valid = "{\"a\":1,\"b\":\"two\"}";
        Assert.True(ToolArgumentJsonRepair.TryRepair(valid, out var json));
        Assert.Equal(valid, json);
    }

    [Fact]
    public void Realistic_Edit_File_Arguments_Repair()
    {
        // A plausible weak-model emission: fenced, single-quoted, trailing comma.
        var raw = "```json\n{'file_path': 'django/db/models/query.py', 'old_string': 'def all(self):', 'new_string': 'def all(self, *, force=False):',}\n```";
        var args = ToolArgumentJsonRepair.ParseArguments(raw);
        Assert.Equal("django/db/models/query.py", args["file_path"]);
        Assert.Equal("def all(self):", args["old_string"]);
        Assert.Equal("def all(self, *, force=False):", args["new_string"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Blank_Input_Is_Not_Repairable(string? raw)
    {
        Assert.False(ToolArgumentJsonRepair.TryRepair(raw, out _));
        Assert.Empty(ToolArgumentJsonRepair.ParseArguments(raw));
    }

    [Theory]
    [InlineData("this is not json at all")]
    [InlineData("I cannot complete this request.")]
    [InlineData("[1, 2, 3]")]            // a top-level array is not an arguments object
    [InlineData("42")]                    // a bare number
    public void Non_Object_Or_Garbage_Is_Not_Repairable(string raw)
    {
        Assert.False(ToolArgumentJsonRepair.TryRepair(raw, out _));
    }

    [Fact]
    public void Empty_Object_Is_Valid()
    {
        Assert.True(ToolArgumentJsonRepair.TryRepair("{}", out var json));
        Assert.Equal("{}", json);
        Assert.Empty(ToolArgumentJsonRepair.ParseArguments("{}"));
    }
}
