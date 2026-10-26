using System.Text.RegularExpressions;
using TuiCode.Syntax;

namespace TuiCode.Editor;

/// <summary>A block that folds (#477): <see cref="Start"/> stays showing and the lines after it through <see cref="End"/> hide.</summary>
/// <param name="ShowsEnd">Whether the folded row repeats the last line, a closing <c>}</c> or <c>#endregion</c>.</param>
public readonly record struct FoldRange(int Start, int End, bool ShowsEnd);

/// <summary>
/// Where a file's blocks are, worked out from its text (#477): by indentation, with the sticky lines' rule for an
/// opening brace and a closing line; a run of imports; between the region markers the language's configuration names;
/// and in Markdown, from each heading to the next of the same or a higher level.
/// </summary>
public sealed class FoldRanges
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly string[] Imports = ["using ", "import ", "from ", "#include ", "use "];

    private readonly bool _markdown;
    private readonly bool _offSide;
    private readonly Regex? _start;
    private readonly Regex? _end;
    private readonly int _tabWidth;

    public FoldRanges(SyntaxLanguage? language, int tabWidth)
    {
        _markdown = language?.Id == "markdown";
        _offSide = language?.Folding?.OffSide == true;
        _tabWidth = Math.Max(tabWidth, 1);
        if (language?.Folding is { Start: { } start, End: { } end })
        {
            _start = Pattern(start);
            _end = Pattern(end);
        }
    }

    /// <summary>Every block, by first line and then outermost first.</summary>
    public IReadOnlyList<FoldRange> Find(IReadOnlyList<string> lines)
    {
        List<FoldRange> ranges = [];
        AddRegions(lines, ranges);
        if (_markdown)
        {
            AddSections(lines, ranges);
        }
        else
        {
            AddImports(lines, ranges);
            for (var i = 0; i < lines.Count; i++)
                if (Block(lines, i) is { } range) ranges.Add(range);
        }
        ranges.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));
        return [.. ranges.Where((r, i) => i == 0 || ranges[i - 1].Start != r.Start)];
    }

    /// <summary>Whether a block starts on <paramref name="line"/>, looking only as far ahead as it needs to.</summary>
    public bool StartsBlock(IReadOnlyList<string> lines, int line)
    {
        if (line < 0 || line >= lines.Count) return false;
        if (_start is not null && IsMatch(_start, lines[line])) return true;
        if (!_markdown) return OpensIndentedBlock(lines, line) || StartsImports(lines, line);
        if (HeadingLevel(lines[line]) is not { } level) return false;
        var next = NextNonBlank(lines, line + 1);
        if (next >= lines.Count) return false;
        return HeadingLevel(lines[next]) is not { } after || after > level;
    }

    /// <summary>What a folded row shows after its first line: <c> { ⋯ }</c>, <c> ⋯ #endregion</c> or just <c> ⋯</c>.</summary>
    public static string Summary(IReadOnlyList<string> lines, int start, int end, bool showsEnd)
    {
        var next = NextNonBlank(lines, start + 1);
        var brace = next < end && lines[next].AsSpan().Trim() is "{";
        var closing = showsEnd && end < lines.Count ? " " + lines[end].Trim() : "";
        return (brace ? " {" : "") + " ⋯" + closing;
    }

    private FoldRange? Block(IReadOnlyList<string> lines, int start)
    {
        if (!OpensIndentedBlock(lines, start)) return null;
        var indent = Indent(lines[start]);
        var end = start;
        var i = NextNonBlank(lines, start + 1);
        if (BraceAt(lines, i, indent)) end = i++;
        for (; i < lines.Count; i++)
        {
            if (IsBlank(lines[i])) continue;
            if (Indent(lines[i]) <= indent) break;
            end = i;
        }
        if (i < lines.Count && Indent(lines[i]) == indent && IsClosing(lines[i]))
            return new FoldRange(start, i, ShowsEnd: true);
        if (!_offSide)
            end = i < lines.Count ? i - 1 : lines.Count - 1;
        return new FoldRange(start, end, ShowsEnd: false);
    }

    // Deeper lines follow, after a brace of its own at its indent if it has one; a line that is that brace isn't a block of its own.
    private bool OpensIndentedBlock(IReadOnlyList<string> lines, int line)
    {
        var text = lines[line];
        if (IsBlank(text) || IsBraceOfHeader(lines, line)) return false;
        var indent = Indent(text);
        var next = NextNonBlank(lines, line + 1);
        if (BraceAt(lines, next, indent)) next = NextNonBlank(lines, next + 1);
        return next < lines.Count && Indent(lines[next]) > indent;
    }

    private bool IsBraceOfHeader(IReadOnlyList<string> lines, int line)
    {
        var indent = Indent(lines[line]);
        if (!BraceAt(lines, line, indent)) return false;
        var previous = line - 1;
        while (previous >= 0 && IsBlank(lines[previous])) previous--;
        return previous >= 0 && Indent(lines[previous]) == indent && !IsClosing(lines[previous]);
    }

    private bool BraceAt(IReadOnlyList<string> lines, int line, int indent) =>
        line < lines.Count && Indent(lines[line]) == indent && lines[line].AsSpan().TrimStart().StartsWith("{");

    private static void AddImports(IReadOnlyList<string> lines, List<FoldRange> ranges)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!StartsImports(lines, i)) continue;
            var end = i;
            for (var next = NextNonBlank(lines, i + 1); next < lines.Count && IsImport(lines[next]); next = NextNonBlank(lines, next + 1))
                end = next;
            ranges.Add(new FoldRange(i, end, ShowsEnd: false));
            i = end;
        }
    }

    private static bool StartsImports(IReadOnlyList<string> lines, int line)
    {
        if (!IsImport(lines[line])) return false;
        var previous = line - 1;
        while (previous >= 0 && IsBlank(lines[previous])) previous--;
        var next = NextNonBlank(lines, line + 1);
        return (previous < 0 || !IsImport(lines[previous])) && next < lines.Count && IsImport(lines[next]);
    }

    // using (...) and using var are statements, not imports.
    private static bool IsImport(string line)
    {
        var text = line.AsSpan().TrimStart();
        foreach (var keyword in Imports)
            if (text.StartsWith(keyword, StringComparison.Ordinal))
                return keyword != "using " || !text.Contains('(') && !text.StartsWith("using var ");
        return false;
    }

    private void AddRegions(IReadOnlyList<string> lines, List<FoldRange> ranges)
    {
        if (_start is null || _end is null) return;
        Stack<int> open = [];
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsMatch(_start, lines[i])) open.Push(i);
            else if (IsMatch(_end, lines[i]) && open.TryPop(out var start)) ranges.Add(new FoldRange(start, i, ShowsEnd: true));
        }
    }

    private static void AddSections(IReadOnlyList<string> lines, List<FoldRange> ranges)
    {
        List<(int Line, int Level)> headings = [];
        string? fence = null;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (fence is not null)
            {
                if (trimmed.StartsWith(fence, StringComparison.Ordinal)) fence = null;
                continue;
            }
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                fence = trimmed[..3];
                continue;
            }
            if (HeadingLevel(lines[i]) is { } level) headings.Add((i, level));
        }

        for (var h = 0; h < headings.Count; h++)
        {
            var (start, level) = headings[h];
            var next = lines.Count;
            for (var k = h + 1; k < headings.Count; k++)
            {
                if (headings[k].Level > level) continue;
                next = headings[k].Line;
                break;
            }
            var end = next - 1;
            while (end > start && IsBlank(lines[end])) end--;
            if (end > start) ranges.Add(new FoldRange(start, end, ShowsEnd: false));
        }
    }

    private static int? HeadingLevel(string line)
    {
        var at = 0;
        while (at < line.Length && at < 3 && line[at] == ' ') at++;
        var level = 0;
        while (at + level < line.Length && line[at + level] == '#') level++;
        if (level is 0 or > 6) return null;
        return at + level == line.Length || line[at + level] is ' ' or '\t' ? level : null;
    }

    private static bool IsClosing(string line)
    {
        var rest = line.AsSpan().TrimStart();
        return rest.StartsWith("}") || rest.StartsWith(")") || rest.StartsWith("]")
               || rest.StartsWith("end") && (rest.Length == 3 || !char.IsLetterOrDigit(rest[3]) && rest[3] != '_');
    }

    private static int NextNonBlank(IReadOnlyList<string> lines, int from)
    {
        while (from < lines.Count && IsBlank(lines[from])) from++;
        return from;
    }

    private static bool IsBlank(string line) => line.AsSpan().IsWhiteSpace();

    private int Indent(string line)
    {
        var columns = 0;
        foreach (var c in line)
        {
            if (c == '\t') columns += _tabWidth - columns % _tabWidth;
            else if (c == ' ') columns++;
            else break;
        }
        return columns;
    }

    private static Regex? Pattern(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsMatch(Regex pattern, string line)
    {
        try
        {
            return pattern.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
