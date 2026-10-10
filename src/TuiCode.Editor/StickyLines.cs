using TuiCode.Syntax;

namespace TuiCode.Editor;

/// <summary>
/// The definitions pinned at the top of the editor as you scroll (#474): which of Go to symbol's
/// definitions enclose a line, by indentation for code and by heading level for Markdown.
/// </summary>
public sealed class StickyLines
{
    /// <summary>The most lines pinned at once; deeper nesting keeps the innermost.</summary>
    public const int Max = 3;

    /// <summary>An editor shorter than this pins nothing.</summary>
    public const int MinHeight = 12;

    public static StickyLines None { get; } = new([]);

    private readonly (int Start, int End)[] _regions;

    private StickyLines((int Start, int End)[] regions) => _regions = regions;

    public bool IsEmpty => _regions.Length == 0;

    /// <summary>Every definition in <paramref name="symbols"/> with a body, and the last line of that body.</summary>
    public static StickyLines From(IReadOnlyList<FileSymbol> symbols, IReadOnlyList<string> lines)
    {
        List<(int Start, int End)> regions = [];
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            if (symbol.Kind is SymbolKind.Field or SymbolKind.EnumMember) continue;
            if (regions.Count > 0 && regions[^1].Start == symbol.Line) continue;
            var end = symbol.Kind == SymbolKind.Heading ? SectionEnd(symbols, i, lines.Count) : BodyEnd(lines, symbol.Line);
            if (end > symbol.Line) regions.Add((symbol.Line, end));
        }
        return new StickyLines([.. regions]);
    }

    /// <summary>The first lines of the definitions enclosing <paramref name="line"/>, outermost first.</summary>
    public IReadOnlyList<int> Enclosing(int line)
    {
        List<int> starts = [];
        foreach (var (start, end) in _regions)
        {
            if (start >= line) break;
            if (end >= line) starts.Add(start);
        }
        return starts;
    }

    /// <summary>
    /// The lines pinned over a view whose top row is <paramref name="top"/>, outermost first. Each one has
    /// scrolled up past its row and still encloses the first line left showing below them;
    /// <paramref name="lineAt"/> maps a row to its file line.
    /// </summary>
    public IReadOnlyList<int> At(int top, Func<int, int> lineAt)
    {
        List<int> pinned = [];
        while (pinned.Count < Max)
        {
            var enclosing = Enclosing(lineAt(top + pinned.Count + 1)).TakeLast(Max).ToList();
            if (enclosing.Count <= pinned.Count || !enclosing.Take(pinned.Count).SequenceEqual(pinned)) break;
            // A definition on the row it would be pinned to is already showing.
            var next = enclosing[pinned.Count];
            if (next >= lineAt(top + pinned.Count)) break;
            pinned.Add(next);
        }
        return pinned;
    }

    // A heading runs until the next one of the same or a higher level.
    private static int SectionEnd(IReadOnlyList<FileSymbol> symbols, int index, int lineCount)
    {
        var heading = symbols[index];
        for (var i = index + 1; i < symbols.Count; i++)
            if (symbols[i].Kind == SymbolKind.Heading && symbols[i].Depth <= heading.Depth)
                return symbols[i].Line - 1;
        return lineCount - 1;
    }

    // The lines indented deeper than the definition, plus an opening brace and the closing line at its own indent.
    private static int BodyEnd(IReadOnlyList<string> lines, int start)
    {
        var indent = Indent(lines[start]);
        var end = start;
        for (var i = start + 1; i < lines.Count; i++)
        {
            var text = lines[i];
            var at = Indent(text);
            if (at == text.Length) continue;
            if (at > indent)
            {
                end = i;
                continue;
            }
            if (at < indent) break;
            var rest = text.AsSpan(at);
            if (rest.StartsWith("{"))
            {
                end = i;
                continue;
            }
            if (rest.StartsWith("}") || rest.StartsWith(")") || rest.StartsWith("]") || IsEnd(rest)) end = i;
            break;
        }
        return end;
    }

    private static bool IsEnd(ReadOnlySpan<char> rest) =>
        rest.StartsWith("end") && (rest.Length == 3 || !char.IsLetterOrDigit(rest[3]) && rest[3] != '_');

    private static int Indent(string text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }
}
