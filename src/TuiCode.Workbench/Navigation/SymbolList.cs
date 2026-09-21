using TuiCode.Syntax;

namespace TuiCode.Workbench.Navigation;

/// <summary>The rows of the <c>gs</c> picker: which definitions a filter leaves, in file order.</summary>
internal static class SymbolList
{
    /// <summary>CamelHumps on the name, as in the Open dialog. File order is kept rather than sorted by score.</summary>
    public static IReadOnlyList<FileSymbol> Filter(IReadOnlyList<FileSymbol> symbols, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0 ? symbols : symbols.Where(s => CamelHumps.Score(filter, s.Name) is not null).ToList();
    }
}
