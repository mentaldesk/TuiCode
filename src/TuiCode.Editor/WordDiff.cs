using System.Globalization;
using System.Text;

namespace TuiCode.Editor;

/// <summary>A run of UTF-16 chars in a line.</summary>
internal readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>The words that differ between the two sides of an edited line (#369).</summary>
internal static class WordDiff
{
    /// <summary>Below this share of characters in common, a line counts as rewritten and gets no word marks.</summary>
    public const double MinSimilarity = 0.4;

    /// <summary>
    /// The changed words on each side, as char ranges; none when the lines share too little to compare.
    /// Leading whitespace is left out, so re-indenting marks nothing.
    /// </summary>
    public static (TextRange[] Left, TextRange[] Right) Changes(string left, string right)
    {
        var leftWords = Words(left, Indent(left));
        var rightWords = Words(right, Indent(right));
        var hunks = LineDiff.Hunks(Texts(left, leftWords), Texts(right, rightWords), LineDiff.MaxEdits);
        if (hunks.Count == 0) return ([], []);

        var changed = 0;
        var leftMarks = new List<TextRange>();
        var rightMarks = new List<TextRange>();
        foreach (var hunk in hunks)
        {
            changed += Span(leftWords, hunk.OldStart, hunk.OldCount, leftMarks) + Span(rightWords, hunk.NewStart, hunk.NewCount, rightMarks);
        }
        var total = left.Length - Indent(left) + right.Length - Indent(right);
        return Similarity(total, changed) < MinSimilarity ? ([], []) : ([.. leftMarks], [.. rightMarks]);
    }

    /// <summary>The share of <paramref name="total"/> chars, across both sides, that isn't <paramref name="changed"/>.</summary>
    public static double Similarity(int total, int changed) => total == 0 ? 1 : (double)(total - changed) / total;

    /// <summary>
    /// Splits <paramref name="line"/> from <paramref name="start"/> into words: runs of letters, digits
    /// and <c>_</c>, runs of whitespace, and single other characters.
    /// </summary>
    public static List<TextRange> Words(string line, int start = 0)
    {
        var words = new List<TextRange>();
        var kind = Kind.Other;
        var elements = StringInfo.GetTextElementEnumerator(line, start);
        while (elements.MoveNext())
        {
            var index = start + elements.ElementIndex;
            var length = elements.GetTextElement().Length;
            var next = KindOf(Rune.GetRuneAt(line, index));
            if (words.Count > 0 && next == kind && kind != Kind.Other)
                words[^1] = words[^1] with { Length = words[^1].Length + length };
            else
                words.Add(new TextRange(index, length));
            kind = next;
        }
        return words;
    }

    private enum Kind { Word, Space, Other }

    private static Kind KindOf(Rune rune) =>
        Rune.IsLetterOrDigit(rune) || rune.Value == '_' ? Kind.Word
        : Rune.IsWhiteSpace(rune) ? Kind.Space
        : Kind.Other;

    private static int Indent(string line)
    {
        var i = 0;
        while (i < line.Length && char.IsWhiteSpace(line[i]))
            i++;
        return i;
    }

    private static string[] Texts(string line, List<TextRange> words) =>
        words.Select(word => line.Substring(word.Start, word.Length)).ToArray();

    private static int Span(List<TextRange> words, int start, int count, List<TextRange> marks)
    {
        if (count == 0) return 0;
        var first = words[start].Start;
        var end = words[start + count - 1].End;
        marks.Add(new TextRange(first, end - first));
        return end - first;
    }
}
