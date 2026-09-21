using TuiCode.Syntax;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Tests;

public class SymbolListTests
{
    private static readonly IReadOnlyList<FileSymbol> Outline =
    [
        new("WorkbenchHost", SymbolKind.Class, 30, 0),
        new("ActiveGroup", SymbolKind.Property, 83, 1),
        new("CompareToSaved", SymbolKind.Method, 1060, 1),
    ];

    [Theory]
    [InlineData("dwa", new[] { "DoWorkAsync", "DoWarnAboutStaleCache" })]
    [InlineData("stale", new[] { "DoWarnAboutStaleCache" })]
    [InlineData("", new[] { "DoWorkAsync", "DoWarnAboutStaleCache", "Count" })]
    [InlineData("zz", new string[0])]
    public void Filtering_keeps_file_order(string filter, string[] expected)
    {
        IReadOnlyList<FileSymbol> symbols =
        [
            new("DoWorkAsync", SymbolKind.Method, 3, 1),
            new("DoWarnAboutStaleCache", SymbolKind.Method, 7, 1),
            new("Count", SymbolKind.Property, 9, 1),
        ];

        Assert.Equal(expected, SymbolList.Filter(symbols, filter).Select(s => s.Name));
    }

    [Fact]
    public void Rendering_right_aligns_the_kind_and_the_one_based_line_and_indents_by_depth()
    {
        Assert.Equal(
            [
                "WorkbenchHost                class    31",
                "  ActiveGroup             property    84",
                "  CompareToSaved            method  1061",
            ],
            SymbolList.Render(Outline, 40, indent: true));
    }

    [Fact]
    public void Each_containing_type_indents_its_members_one_more_level()
    {
        IReadOnlyList<FileSymbol> nested =
        [
            new("Outer", SymbolKind.Class, 0, 0),
            new("Field", SymbolKind.Property, 2, 1),
            new("Inner", SymbolKind.Class, 4, 1),
            new("Deep", SymbolKind.Method, 6, 2),
        ];

        Assert.Equal(
            [
                "Outer                               class  1",
                "  Field                          property  3",
                "  Inner                             class  5",
                "    Deep                           method  7",
            ],
            SymbolList.Render(nested, 44, indent: true));
    }

    [Fact]
    public void Flat_rows_drop_the_indent_but_keep_the_columns()
    {
        Assert.Equal(
            [
                "WorkbenchHost                class    31",
                "ActiveGroup               property    84",
                "CompareToSaved              method  1061",
            ],
            SymbolList.Render(Outline, 40, indent: false));
    }

    [Fact]
    public void A_name_too_long_for_the_space_is_truncated_rather_than_pushing_the_columns_out()
    {
        var rows = SymbolList.Render(Outline, 24, indent: true);

        Assert.Equal(
            [
                "Workben…     class    31",
                "  Activ…  property    84",
                "  Compa…    method  1061",
            ],
            rows);
        Assert.All(rows, r => Assert.Equal(24, r.Length));
    }

    [Fact]
    public void Too_narrow_for_the_columns_leaves_the_line_number()
    {
        Assert.Equal(["        31", "        84", "      1061"], SymbolList.Render(Outline, 10, indent: true));
    }

    [Fact]
    public void Nothing_to_show_renders_no_rows()
    {
        Assert.Empty(SymbolList.Render([], 40, indent: true));
        Assert.Empty(SymbolList.Render(Outline, 0, indent: true));
    }
}
