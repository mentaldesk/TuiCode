using TuiCode.Syntax;

namespace TuiCode.Workbench.Navigation;

/// <summary>The rows of the <c>gs</c> picker: which definitions a filter leaves, in file order, and how each reads.</summary>
internal static class SymbolList
{
    private const int Gap = 2;
    private const int IndentWidth = 2;

    /// <summary>CamelHumps on the name, as in the Open dialog. File order is kept rather than sorted by score.</summary>
    public static IReadOnlyList<FileSymbol> Filter(IReadOnlyList<FileSymbol> symbols, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0 ? symbols : symbols.Where(s => CamelHumps.Score(filter, s.Name) is not null).ToList();
    }

    /// <summary>
    /// One string per symbol: the name, indented by its depth and by the <paramref name="iconWidth"/> columns
    /// <see cref="SymbolListSource"/> draws the kind icon in, then the kind and the 1-based line right-aligned
    /// in columns as wide as the widest of each. The name gives up what's left over and is truncated with an
    /// ellipsis, so the two columns stay aligned whatever the picker's width.
    /// </summary>
    public static IReadOnlyList<string> Render(IReadOnlyList<FileSymbol> symbols, int width, bool indent, int iconWidth = 0)
    {
        if (symbols.Count == 0 || width <= 0) return [];
        var kinds = symbols.Select(s => Label(s.Kind)).ToArray();
        var lines = symbols.Select(s => (s.Line + 1).ToString()).ToArray();
        var kindWidth = kinds.Max(k => k.Length);
        var lineWidth = lines.Max(l => l.Length);
        var nameWidth = NameWidth(symbols, width);

        var rows = new string[symbols.Count];
        for (var i = 0; i < symbols.Count; i++)
        {
            var name = new string(' ', IconColumn(symbols[i], indent) + iconWidth) + symbols[i].Name;
            var row = Truncate(name, nameWidth).PadRight(nameWidth)
                + kinds[i].PadLeft(Gap + kindWidth)
                + lines[i].PadLeft(Gap + lineWidth);
            // Narrower than the columns themselves: the line number is the part worth keeping whole.
            rows[i] = row.Length <= width ? row : Truncate(lines[i], width).PadLeft(width);
        }
        return rows;
    }

    /// <summary>What's left of <paramref name="width"/> for the name once the kind and line columns have taken theirs.</summary>
    public static int NameWidth(IReadOnlyList<FileSymbol> symbols, int width)
    {
        if (symbols.Count == 0 || width <= 0) return 0;
        var kindWidth = symbols.Max(s => Label(s.Kind).Length);
        var lineWidth = symbols.Max(s => (s.Line + 1).ToString().Length);
        return Math.Max(0, width - (Gap + kindWidth + Gap + lineWidth));
    }

    /// <summary>The column a row's icon is drawn in: after its indent, ahead of its name.</summary>
    public static int IconColumn(FileSymbol symbol, bool indent) => indent ? IndentWidth * symbol.Depth : 0;

    private static string Label(SymbolKind kind) => kind switch
    {
        SymbolKind.Class => "class",
        SymbolKind.Interface => "interface",
        SymbolKind.Enum => "enum",
        SymbolKind.Struct => "struct",
        SymbolKind.Trait => "trait",
        SymbolKind.Method => "method",
        _ => "property",
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
