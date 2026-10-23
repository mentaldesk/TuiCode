using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspHoverTests
{
    [Fact]
    public void Parse_splits_markdown_into_its_fenced_code_and_the_prose_around_it()
    {
        var parts = Parse("""{"contents": {"kind": "markdown", "value": "```csharp\nstring Planner.Resolve(string request)\n```\n\nWorks out the steps.\n\nParameters:\n- ``request``: What was asked for."}}""");

        Assert.Equal(
        [
            new HoverPart("csharp", "string Planner.Resolve(string request)"),
            new HoverPart(null, "Works out the steps.\n\nParameters:\n- request: What was asked for."),
        ], parts);
    }

    [Fact]
    public void Parse_keeps_plaintext_markup_as_it_is()
    {
        var parts = Parse("""{"contents": {"kind": "plaintext", "value": "int x\n```not a fence``` \\_"}}""");

        Assert.Equal([new HoverPart(null, "int x\n```not a fence``` \\_")], parts);
    }

    [Fact]
    public void Parse_reads_a_plain_string_as_markdown()
    {
        var parts = Parse("""{"contents": "```python\ndef f(): ...\n```\nDoes \\_things\\_."}""");

        Assert.Equal([new HoverPart("python", "def f(): ..."), new HoverPart(null, "Does _things_.")], parts);
    }

    [Fact]
    public void Parse_reads_a_marked_string_as_code_in_its_language()
    {
        var parts = Parse("""{"contents": {"language": "typescript", "value": "function f(): void"}}""");

        Assert.Equal([new HoverPart("typescript", "function f(): void")], parts);
    }

    [Fact]
    public void Parse_reads_each_of_an_array_of_marked_strings_in_turn()
    {
        var parts = Parse("""{"contents": [{"language": "python", "value": "(function) def f() -> None"}, "---", "Says hello."]}""");

        Assert.Equal([new HoverPart("python", "(function) def f() -> None"), new HoverPart(null, "Says hello.")], parts);
    }

    [Fact]
    public void Parse_leaves_a_fence_without_a_language_as_code()
    {
        var parts = Parse("""{"contents": {"kind": "markdown", "value": "```\nlet x = 1\n```"}}""");

        Assert.Equal([new HoverPart("", "let x = 1")], parts);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"contents": ""}""")]
    [InlineData("""{"contents": []}""")]
    [InlineData("""{"contents": {"kind": "markdown", "value": "  \n\n"}}""")]
    public void Parse_gives_nothing_for_an_empty_answer(string json) =>
        Assert.Empty(Parse(json));

    private static IReadOnlyList<HoverPart> Parse(string json) => LspHover.Parse(JsonNode.Parse(json));
}
