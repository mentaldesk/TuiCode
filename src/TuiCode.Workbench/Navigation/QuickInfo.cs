using System.Drawing;
using System.Globalization;
using Terminal.Gui.Text;
using TuiCode.Editor;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Navigation;

/// <summary>A row of the box: prose, or row <see cref="Offset"/> chars into line <see cref="Line"/> of code block <see cref="Block"/>.</summary>
public sealed record QuickInfoRow(string Text, int Block = -1, int Line = -1, int Offset = 0)
{
    public bool IsCode => Block >= 0;
}

public sealed record QuickInfoBlock(string Language, IReadOnlyList<string> Lines);

/// <summary>Quick info's answer laid out for its box: the signature's code rows, then the doc comment wrapped.</summary>
public sealed record QuickInfo(IReadOnlyList<QuickInfoRow> Rows, IReadOnlyList<QuickInfoBlock> Blocks)
{
    public const int MaxHeight = 12;

    // Prose wraps at a reading width unless the code is already wider.
    public const int ProseWidth = 80;

    private const int TabWidth = 4;

    /// <summary>Border and a column of space either side.</summary>
    public const int Frame = 4;

    public int Width => Rows.Count == 0 ? 0 : Rows.Max(r => r.Text.GetColumns());

    public static QuickInfo Lay(IReadOnlyList<HoverPart> parts, int maxWidth)
    {
        maxWidth = Math.Max(maxWidth, 1);
        var code = parts.Where(p => p.IsCode).SelectMany(p => p.Text.Split('\n')).Select(l => Expand(l).GetColumns()).DefaultIfEmpty(0).Max();
        var proseWidth = Math.Min(maxWidth, Math.Max(code, ProseWidth));
        var rows = new List<QuickInfoRow>();
        var blocks = new List<QuickInfoBlock>();
        foreach (var part in parts)
        {
            if (rows.Count > 0) rows.Add(new QuickInfoRow(""));
            if (part.IsCode)
            {
                var lines = part.Text.Split('\n').Select(Expand).ToList();
                var block = blocks.Count;
                blocks.Add(new QuickInfoBlock(part.Language!, lines));
                for (var line = 0; line < lines.Count; line++) WrapCode(rows, block, line, lines[line], maxWidth);
                continue;
            }
            var blank = false;
            foreach (var line in part.Text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (!blank) rows.Add(new QuickInfoRow(""));
                    blank = true;
                    continue;
                }
                blank = false;
                Wrap(rows, Expand(line).TrimEnd(), proseWidth);
            }
        }
        return new QuickInfo(rows, blocks);
    }

    /// <summary>
    /// Where the box goes in <paramref name="editor"/>: under the caret's row if it fits, else above when there's more room
    /// there, as wide as <paramref name="width"/> asks and at most <see cref="MaxHeight"/> rows. All in screen cells.
    /// </summary>
    public static Rectangle Place(Rectangle editor, Point caret, int width, int height)
    {
        height = Math.Min(height, MaxHeight);
        var below = editor.Bottom - caret.Y - 1;
        var above = caret.Y - editor.Top;
        var top = height <= below || below >= above ? caret.Y + 1 : caret.Y - Math.Min(height, above);
        height = Math.Max(Math.Min(height, top > caret.Y ? below : above), 0);
        width = Math.Min(width, editor.Width);
        var left = Math.Clamp(caret.X, editor.Left, Math.Max(editor.Right - width, editor.Left));
        return new Rectangle(left, top, width, height);
    }

    private static string Expand(string line) => line.Replace("\t", new string(' ', TabWidth));

    private static void Wrap(List<QuickInfoRow> rows, string text, int width)
    {
        var graphemes = Graphemes(text);
        var starts = WrapLayout.RowStarts(graphemes, width, TabWidth);
        for (var row = 0; row < starts.Length; row++)
        {
            var end = row + 1 < starts.Length ? starts[row + 1] : graphemes.Count;
            rows.Add(new QuickInfoRow(string.Concat(graphemes.Skip(starts[row]).Take(end - starts[row])).TrimEnd()));
        }
    }

    private static void WrapCode(List<QuickInfoRow> rows, int block, int line, string text, int width)
    {
        var graphemes = Graphemes(text);
        var starts = WrapLayout.RowStarts(graphemes, width, TabWidth);
        var offset = 0;
        for (var row = 0; row < starts.Length; row++)
        {
            var end = row + 1 < starts.Length ? starts[row + 1] : graphemes.Count;
            var piece = string.Concat(graphemes.Skip(starts[row]).Take(end - starts[row]));
            rows.Add(new QuickInfoRow(piece.TrimEnd(), block, line, offset));
            offset += piece.Length;
        }
    }

    private static List<string> Graphemes(string text)
    {
        var result = new List<string>(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) result.Add(enumerator.GetTextElement());
        return result;
    }
}
