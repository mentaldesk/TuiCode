namespace TuiCode.Editor;

/// <summary>Which part of a conflict a keep command leaves in the file (#693).</summary>
public enum ConflictKeep
{
    Current,
    Incoming,
    Both,
}

/// <summary>
/// A conflict block git wrote into a file (#693), by line: <see cref="Start"/> is the <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c> line,
/// <see cref="Base"/> the diff3 <c>|||||||</c> line if there is one, <see cref="Separator"/> the <c>=======</c> line and
/// <see cref="End"/> the <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> line.
/// </summary>
public sealed record MergeConflict(int Start, int? Base, int Separator, int End, string CurrentName, string IncomingName)
{
    public bool Contains(int line) => line >= Start && line <= End;

    public (int First, int Count) Current => (Start + 1, (Base ?? Separator) - Start - 1);

    public (int First, int Count) BaseLines => Base is { } at ? (at + 1, Separator - at - 1) : (Separator, 0);

    public (int First, int Count) Incoming => (Separator + 1, End - Separator - 1);

    /// <summary>The lines that replace the whole block when <paramref name="keep"/> is chosen.</summary>
    public IReadOnlyList<string> Kept(IReadOnlyList<string> lines, ConflictKeep keep)
    {
        var current = Slice(lines, Current);
        var incoming = Slice(lines, Incoming);
        return keep switch
        {
            ConflictKeep.Current => current,
            ConflictKeep.Incoming => incoming,
            _ => [.. current, .. incoming],
        };
    }

    private static string[] Slice(IReadOnlyList<string> lines, (int First, int Count) range) =>
        [.. Enumerable.Range(range.First, range.Count).Select(i => lines[i])];
}

/// <summary>Finds conflict blocks in a file's lines (#693), from the markers alone: no git, no language.</summary>
public static class MergeConflicts
{
    private const int MarkerLength = 7;

    /// <summary>Whether <paramref name="line"/> is any of the four marker lines, which is all <see cref="Find"/> looks at.</summary>
    public static bool IsMarker(string line) => Kind(line) is not null;

    /// <summary>Every complete block, in order. A block missing any of its marker lines is left out.</summary>
    public static IReadOnlyList<MergeConflict> Find(IReadOnlyList<string> lines)
    {
        var found = new List<MergeConflict>();
        int? start = null, @base = null, separator = null;
        var currentName = string.Empty;
        for (var i = 0; i < lines.Count; i++)
        {
            switch (Kind(lines[i]))
            {
                case '<':
                    (start, @base, separator, currentName) = (i, null, null, Name(lines[i]));
                    break;
                case '|' when start is not null && @base is null && separator is null:
                    @base = i;
                    break;
                case '=' when start is not null && separator is null:
                    separator = i;
                    break;
                case '>' when start is not null:
                    if (separator is { } at) found.Add(new MergeConflict(start.Value, @base, at, i, currentName, Name(lines[i])));
                    (start, @base, separator) = (null, null, null);
                    break;
            }
        }
        return found;
    }

    /// <summary>How many complete blocks <paramref name="text"/> has, whatever its line endings.</summary>
    public static int Count(string text) => Find(text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray()).Count;

    /// <summary>The conflict <paramref name="line"/> is in, or null.</summary>
    public static MergeConflict? At(IReadOnlyList<MergeConflict> conflicts, int line) =>
        conflicts.FirstOrDefault(c => c.Contains(line));

    private static char? Kind(string line)
    {
        if (line.Length < MarkerLength) return null;
        var c = line[0];
        if (c is not ('<' or '|' or '=' or '>')) return null;
        for (var i = 1; i < MarkerLength; i++)
            if (line[i] != c) return null;
        if (line.Length == MarkerLength) return c;
        if (!char.IsWhiteSpace(line[MarkerLength])) return null;
        return c != '=' || string.IsNullOrWhiteSpace(line[MarkerLength..]) ? c : null;
    }

    private static string Name(string line) => line.Length > MarkerLength ? line[MarkerLength..].Trim() : string.Empty;
}
