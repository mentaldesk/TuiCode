using TuiCode.Abstractions;
using TuiCode.Syntax;

namespace TuiCode.Workbench.Navigation;

/// <summary>How each row of the <c>gs</c> picker reads.</summary>
internal static class SymbolList
{
    private const int Gap = 2;
    private const int IndentWidth = 2;

    /// <summary>
    /// One string per symbol: the name, indented by its depth and led by its kind icon, then the kind and the
    /// 1-based line right-aligned in columns as wide as the widest of each. The name gives up what's left over
    /// and is truncated with an ellipsis, so the two columns stay aligned whatever the picker's width.
    /// </summary>
    public static IReadOnlyList<string> Render(IReadOnlyList<FileSymbol> symbols, int width, bool indent, FileIconStyle icons = FileIconStyle.Off)
    {
        if (symbols.Count == 0 || width <= 0) return [];
        var columns = Columns.Of(symbols);
        return [.. symbols.Select(s => Row(s, columns, width, indent, icons))];
    }

    public static string Row(FileSymbol symbol, Columns columns, int width, bool indent, FileIconStyle icons)
    {
        var kind = Label(symbol.Kind);
        var line = (symbol.Line + 1).ToString();
        var nameWidth = Math.Max(0, width - (Gap + columns.Kind + Gap + columns.Line));
        var iconWidth = SymbolIcons.Width(icons);
        var icon = (SymbolIcons.For(symbol.Kind, icons) ?? "").PadRight(iconWidth);
        var name = new string(' ', IconColumn(symbol, indent)) + icon + symbol.Name;
        var row = Truncate(name, nameWidth).PadRight(nameWidth)
            + kind.PadLeft(Gap + columns.Kind)
            + line.PadLeft(Gap + columns.Line);
        // Narrower than the columns themselves: the line number is the part worth keeping whole.
        return row.Length <= width ? row : Truncate(line, width).PadLeft(width);
    }

    public sealed record Columns(int Kind, int Line)
    {
        public static Columns Of(IReadOnlyList<FileSymbol> symbols) => symbols.Count == 0
            ? new(0, 0)
            : new(symbols.Max(s => Label(s.Kind).Length), symbols.Max(s => (s.Line + 1).ToString().Length));
    }

    private static int IconColumn(FileSymbol symbol, bool indent) => indent ? IndentWidth * symbol.Depth : 0;

    private static string Label(SymbolKind kind) => kind switch
    {
        SymbolKind.Class => "class",
        SymbolKind.Interface => "interface",
        SymbolKind.Enum => "enum",
        SymbolKind.Struct => "struct",
        SymbolKind.Trait => "trait",
        SymbolKind.Method => "method",
        SymbolKind.Field => "field",
        SymbolKind.EnumMember => "enum member",
        SymbolKind.Heading => "heading",
        _ => "property",
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
