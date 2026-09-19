using System.Globalization;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

/// <summary>Counts for a buffer or selection (#123). Characters are graphemes, the unit of the editor's columns; line breaks aren't counted.</summary>
public readonly record struct DocumentStats(int Lines, int Words, int Characters, int NonWhitespaceCharacters)
{
    public static DocumentStats Of(IReadOnlyList<string> lines)
    {
        var counter = new Counter();
        foreach (var line in lines)
            counter.Add(line);
        return counter.Stats(lines.Count);
    }

    /// <summary>
    /// Counts the text covered by <paramref name="ranges"/> (column X, row Y, in graphemes), once where they overlap.
    /// A range ending at column 0 doesn't count that line.
    /// </summary>
    public static DocumentStats Of(IReadOnlyList<string> lines, IEnumerable<(Point Start, Point End)> ranges)
    {
        var counter = new Counter();
        var rows = 0;
        var lastRow = -1;
        foreach (var (start, end) in Merge(ranges))
        {
            var first = Math.Max(start.Y, lastRow + 1);
            var last = end.X == 0 && end.Y > start.Y ? end.Y - 1 : end.Y;
            if (last >= first)
            {
                rows += last - first + 1;
                lastRow = last;
            }

            for (var row = start.Y; row <= end.Y; row++)
            {
                var line = lines[row];
                var from = row == start.Y ? CharIndex(line, start.X) : 0;
                var to = row == end.Y ? CharIndex(line, end.X) : line.Length;
                counter.Add(line.AsSpan(from, to - from));
            }
        }
        return counter.Stats(rows);
    }

    private static List<(Point Start, Point End)> Merge(IEnumerable<(Point Start, Point End)> ranges)
    {
        var merged = new List<(Point Start, Point End)>();
        foreach (var (start, end) in ranges.Where(r => r.Start != r.End).OrderBy(r => r.Start.Y).ThenBy(r => r.Start.X))
        {
            if (merged.Count > 0 && !Before(merged[^1].End, start))
            {
                if (Before(merged[^1].End, end)) merged[^1] = (merged[^1].Start, end);
            }
            else
            {
                merged.Add((start, end));
            }
        }
        return merged;
    }

    private static bool Before(Point a, Point b) => a.Y < b.Y || (a.Y == b.Y && a.X < b.X);

    private static int CharIndex(string line, int column)
    {
        var index = 0;
        for (var i = 0; i < column && index < line.Length; i++)
            index += StringInfo.GetNextTextElementLength(line.AsSpan(index));
        return index;
    }

    private struct Counter
    {
        private int _words;
        private int _characters;
        private int _nonWhitespace;

        public readonly DocumentStats Stats(int lines) => new(lines, _words, _characters, _nonWhitespace);

        // Each call ends any word in progress, as a line break would.
        public void Add(ReadOnlySpan<char> text)
        {
            var inWord = false;
            while (!text.IsEmpty)
            {
                var space = char.IsWhiteSpace(text[0]);
                _characters++;
                if (!space) _nonWhitespace++;
                if (!space && !inWord) _words++;
                inWord = !space;
                text = text[StringInfo.GetNextTextElementLength(text)..];
            }
        }
    }
}
