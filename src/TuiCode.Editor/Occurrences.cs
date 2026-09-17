using System.Drawing;

namespace TuiCode.Editor;

/// <summary>Finds text in lines of graphemes, for selecting occurrences (#113). Columns are grapheme indices.</summary>
internal static class Occurrences
{
    private enum Kind { Word, Space, Other }

    /// <summary>The word, or failing that the run of whitespace, that <paramref name="position"/> is in or touches.</summary>
    public static (Point Start, Point End)? RunAt(IReadOnlyList<string[]> lines, Point position)
    {
        var line = lines[position.Y];
        foreach (var kind in (Kind[])[Kind.Word, Kind.Space])
        {
            var at = position.X < line.Length && KindOf(line[position.X]) == kind ? position.X
                : position.X > 0 && position.X <= line.Length && KindOf(line[position.X - 1]) == kind ? position.X - 1
                : -1;
            if (at < 0) continue;

            var start = at;
            while (start > 0 && KindOf(line[start - 1]) == kind) start--;
            var end = at + 1;
            while (end < line.Length && KindOf(line[end]) == kind) end++;
            return (new Point(start, position.Y), new Point(end, position.Y));
        }
        return null;
    }

    /// <summary>
    /// Every non-overlapping occurrence of <paramref name="needle"/> (its lines of graphemes), in buffer order.
    /// <paramref name="whole"/> skips occurrences that are part of a longer word or whitespace run.
    /// </summary>
    public static List<(Point Start, Point End)> Find(IReadOnlyList<string[]> lines, IReadOnlyList<string[]> needle, bool whole)
    {
        var found = new List<(Point, Point)>();
        if (needle.Count == 0 || (needle.Count == 1 && needle[0].Length == 0)) return found;

        var last = needle.Count - 1;
        var resumeColumn = 0;
        for (var row = 0; row + last < lines.Count; row++)
        {
            var line = lines[row];
            var column = resumeColumn;
            resumeColumn = 0;
            for (; column + needle[0].Length <= line.Length; column++)
            {
                if (last > 0) column = Math.Max(column, line.Length - needle[0].Length);
                if (!MatchesAt(lines, needle, row, column)) continue;

                var end = new Point(last == 0 ? column + needle[0].Length : needle[last].Length, row + last);
                if (whole && !IsWhole(lines, needle, new Point(column, row), end)) continue;

                found.Add((new Point(column, row), end));
                if (last > 0)
                {
                    row += last - 1;
                    resumeColumn = end.X;
                    break;
                }
                column = end.X - 1;
            }
        }
        return found;
    }

    private static bool MatchesAt(IReadOnlyList<string[]> lines, IReadOnlyList<string[]> needle, int row, int column)
    {
        var last = needle.Count - 1;
        if (!Same(lines[row], column, needle[0])) return false;
        for (var i = 1; i < last; i++)
            if (lines[row + i].Length != needle[i].Length || !Same(lines[row + i], 0, needle[i]))
                return false;
        return last == 0 || Same(lines[row + last], 0, needle[last]);
    }

    private static bool Same(string[] line, int column, string[] piece)
    {
        if (column + piece.Length > line.Length) return false;
        for (var i = 0; i < piece.Length; i++)
            if (!string.Equals(line[column + i], piece[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    private static bool IsWhole(IReadOnlyList<string[]> lines, IReadOnlyList<string[]> needle, Point start, Point end)
    {
        var first = needle[0].Length > 0 ? KindOf(needle[0][0]) : Kind.Other;
        var lastPiece = needle[^1];
        var final = lastPiece.Length > 0 ? KindOf(lastPiece[^1]) : Kind.Other;
        var before = start.X > 0 ? KindOf(lines[start.Y][start.X - 1]) : Kind.Other;
        var after = end.X < lines[end.Y].Length ? KindOf(lines[end.Y][end.X]) : Kind.Other;
        return (first == Kind.Other || before != first) && (final == Kind.Other || after != final);
    }

    private static Kind KindOf(string grapheme) =>
        grapheme.Length == 0 ? Kind.Other
        : char.IsLetterOrDigit(grapheme, 0) || grapheme[0] == '_' ? Kind.Word
        : char.IsWhiteSpace(grapheme[0]) ? Kind.Space
        : Kind.Other;
}
