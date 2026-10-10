using System.Text.Json.Nodes;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspSymbolsTests
{
    // What csharp-ls 0.28 answers for a static class in a namespace.
    private static readonly JsonNode CsharpLs = JsonNode.Parse("""
        [{"name": "LineDiff.cs", "kind": 1, "range": {"start": {"line": 0, "character": 0}, "end": {"line": 11, "character": 0}},
          "selectionRange": {"start": {"line": 0, "character": 0}, "end": {"line": 11, "character": 0}},
          "children": [{"name": "Demo", "kind": 3, "range": {"start": {"line": 0, "character": 0}, "end": {"line": 11, "character": 0}},
            "selectionRange": {"start": {"line": 0, "character": 10}, "end": {"line": 0, "character": 14}},
            "children": [{"name": "LineDiff", "kind": 5, "range": {"start": {"line": 1, "character": 0}, "end": {"line": 11, "character": 0}},
              "selectionRange": {"start": {"line": 2, "character": 20}, "end": {"line": 2, "character": 28}},
              "children": [
                {"name": "Hunks(string before, string after)", "kind": 6, "range": {"start": {"line": 4, "character": 0}, "end": {"line": 8, "character": 0}},
                 "selectionRange": {"start": {"line": 4, "character": 22}, "end": {"line": 4, "character": 27}}},
                {"name": "Twice(string a)", "kind": 6, "range": {"start": {"line": 8, "character": 0}, "end": {"line": 10, "character": 0}},
                 "selectionRange": {"start": {"line": 9, "character": 22}, "end": {"line": 9, "character": 27}}}]}]}]}]
        """);

    [Theory]
    [InlineData(4, 22, "LineDiff.Hunks")]
    [InlineData(9, 22, "LineDiff.Twice")]
    [InlineData(2, 20, "LineDiff")]
    public void QualifiedName_names_a_declaration_after_its_type_without_namespaces_or_parameters(int line, int character, string name) =>
        Assert.Equal(name, LspSymbols.QualifiedName(CsharpLs, line, character));

    [Fact]
    public void QualifiedName_is_null_where_nothing_is_declared() =>
        Assert.Null(LspSymbols.QualifiedName(CsharpLs, 6, 8));

    [Fact]
    public void QualifiedName_reads_flat_symbol_information_by_its_container()
    {
        var symbols = JsonNode.Parse("""
            [{"name": "LineDiff", "kind": 5, "containerName": "Demo",
              "location": {"uri": "file:///a.cs", "range": {"start": {"line": 2, "character": 0}, "end": {"line": 10, "character": 1}}}},
             {"name": "Hunks", "kind": 6, "containerName": "Demo.LineDiff",
              "location": {"uri": "file:///a.cs", "range": {"start": {"line": 4, "character": 4}, "end": {"line": 7, "character": 5}}}}]
            """);

        Assert.Equal("LineDiff.Hunks", LspSymbols.QualifiedName(symbols, 4, 22));
    }

    [Fact]
    public void QualifiedName_is_null_for_no_answer() =>
        Assert.Null(LspSymbols.QualifiedName(null, 0, 0));
}
