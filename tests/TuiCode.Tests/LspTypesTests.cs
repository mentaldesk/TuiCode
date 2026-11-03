using System.Text.Json.Nodes;
using TuiCode.Syntax;
using TuiCode.Workbench.Languages;

namespace TuiCode.Tests;

public class LspTypesTests
{
    private static readonly string A = Path.GetFullPath("/work/A.cs");
    private static readonly string B = Path.GetFullPath("/work/B.cs");

    [Fact]
    public void Symbol_information_gives_each_type_its_kind_container_and_place()
    {
        var result = Parse("""
            [
              {"name":"LanguageServers","kind":5,"containerName":"TuiCode.Workbench.Languages","location":{"uri":"URI_A","range":{"start":{"line":7,"character":20},"end":{"line":7,"character":35}}}},
              {"name":"IFileSystem","kind":11,"location":{"uri":"URI_B","range":{"start":{"line":2,"character":17},"end":{"line":2,"character":28}}}}
            ]
            """);

        Assert.Equal(
        [
            new TypeSymbol("LanguageServers", SymbolKind.Class, "TuiCode.Workbench.Languages", new SourceLocation(A, 7, 20)),
            new TypeSymbol("IFileSystem", SymbolKind.Interface, null, new SourceLocation(B, 2, 17)),
        ], LspTypes.Parse(result));
    }

    [Fact]
    public void A_workspace_symbol_without_a_range_goes_to_the_top_of_its_file()
    {
        var result = Parse("""[{"name":"Point","kind":23,"containerName":"","location":{"uri":"URI_A"}}]""");

        Assert.Equal([new TypeSymbol("Point", SymbolKind.Struct, null, new SourceLocation(A, 0, 0))], LspTypes.Parse(result));
    }

    [Theory]
    [InlineData(5, SymbolKind.Class)]
    [InlineData(10, SymbolKind.Enum)]
    [InlineData(11, SymbolKind.Interface)]
    [InlineData(23, SymbolKind.Struct)]
    public void Classes_records_interfaces_structs_and_enums_are_types(int kind, SymbolKind expected)
    {
        var result = Parse(Of("T", kind));

        Assert.Equal(expected, Assert.Single(LspTypes.Parse(result)).Kind);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(22)]
    public void Namespaces_methods_properties_fields_functions_and_enum_members_are_not(int kind)
    {
        var result = Parse(Of("m", kind));

        Assert.Empty(LspTypes.Parse(result));
    }

    [Fact]
    public void A_null_result_and_a_library_type_find_nothing()
    {
        Assert.Empty(LspTypes.Parse(null));
        Assert.Empty(LspTypes.Parse(Parse("""[{"name":"Console","kind":5,"location":{"uri":"csharp:/metadata/projects/work/assemblies/System.Console/symbols/System.Console.cs"}}]""")));
    }

    private static string Of(string name, int kind) =>
        $"[{{\"name\":\"{name}\",\"kind\":{kind},\"location\":{{\"uri\":\"URI_A\",\"range\":{{\"start\":{{\"line\":0,\"character\":0}},\"end\":{{\"line\":0,\"character\":1}}}}}}}}]";

    private static JsonNode? Parse(string json) =>
        JsonNode.Parse(json.Replace("URI_A", LspLocations.ToUri(A)).Replace("URI_B", LspLocations.ToUri(B)));
}
