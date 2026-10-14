using Terminal.Gui.Editor.Document;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Tab and Shift+Tab (#14, #386).
internal sealed partial class CodeEditor
{
    public bool InsertSpaces { get; set; }

    private void Indent()
    {
        if (HasSelection) ReplaceSelection("");
        Document!.Insert(CaretOffset, InsertSpaces ? new string(' ', TabWidth - DisplayColumn() % TabWidth) : "\t");
    }

    /// <summary>Indents every line a multi-line selection touches, or else inserts an indent at every caret, as one undo step.</summary>
    public bool IndentLines()
    {
        if (ReadOnly) return true;
        var carets = Carets;
        if (carets.Any(c => c.Start.Y != c.End.Y))
            ShiftLines(carets, outdent: false);
        else if (_secondary.Count == 0)
            EditAtPrimary(Indent);
        else
            AtEachCaret(_ => Indent());
        return true;
    }

    /// <summary>Outdents every line under a caret or selection by up to one level, as one undo step.</summary>
    public bool OutdentLines()
    {
        if (ReadOnly) return true;
        ShiftLines(Carets, outdent: true);
        return true;
    }

    private void ShiftLines(Caret[] carets, bool outdent)
    {
        var unit = InsertSpaces ? new string(' ', TabWidth) : "\t";
        EditLines(carets, blocks =>
        {
            var shifted = new Dictionary<int, (int At, int By)>();
            foreach (var (first, last) in blocks)
            {
                for (var row = first; row <= last; row++)
                {
                    var line = Graphemes(row);
                    if (!outdent)
                    {
                        if (line.Length == 0) continue;
                        SetLine(row, unit + LineText(row));
                        shifted[row] = (0, unit.Length);
                        continue;
                    }
                    var leading = LeadingWhitespace(line);
                    var kept = OutdentedLength(line, leading);
                    if (kept == leading) continue;
                    SetLine(row, string.Concat(line[..kept]) + string.Concat(line[leading..]));
                    shifted[row] = (kept, kept - leading);
                }
            }
            return shifted;
        });
    }

    private void SetLine(int row, string text)
    {
        var line = Document!.GetLineByNumber(row + 1);
        Document.Replace(line.Offset, line.Length, text);
    }

    /// <summary>
    /// Edits the lines under <paramref name="carets"/> as one undo step. The edit reports each row it changed as the column the
    /// change starts at and the columns it added (or removed, if negative), so carets after that column follow their text.
    /// </summary>
    private void EditLines(Caret[] carets, Func<List<(int First, int Last)>, Dictionary<int, (int At, int By)>> edit)
    {
        Edit(() =>
        {
            var shifted = edit(RowBlocks(carets));
            return [.. carets.Select(c => c with { Position = Shifted(c.Position), Anchor = c.Anchor is { } a ? Shifted(a) : null })];

            // A point at the start of the change stays there, so a selection of whole lines still is one.
            Point Shifted(Point point) =>
                shifted.TryGetValue(point.Y, out var shift) && point.X > shift.At
                    ? point with { X = Math.Clamp(point.X + shift.By, shift.At, LineLength(point.Y)) }
                    : point;
        });
    }

    // Trims the leading whitespace from its end back to the previous tab stop, so mixed tabs and spaces stay as they were.
    private int OutdentedLength(string[] line, int leading)
    {
        var width = ColumnsBefore(line, leading);
        var target = Math.Max(width % TabWidth == 0 ? width - TabWidth : width - width % TabWidth, 0);
        var kept = leading;
        while (kept > 0 && ColumnsBefore(line, kept) > target)
            kept--;
        return kept;
    }

    private int DisplayColumn() => ColumnsBefore(Graphemes(CurrentRow), CurrentColumn);
}

// Enter keeps the indentation of the line it was pressed on (#385).
internal sealed partial class CodeEditor
{
    // Whitespace Enter put on an otherwise empty line, as an anchor at the line's start and its length then; it's trimmed once no caret is on that line.
    private readonly List<(TextAnchor Line, int Count)> _autoIndents = [];

    private void NewLineKeepingIndent()
    {
        if (ReadOnly) return;
        if (HasSelection) ReplaceSelection("");

        var row = CurrentRow;
        var line = Graphemes(row);
        var column = Math.Min(CurrentColumn, line.Length);
        var leading = LeadingWhitespace(line);
        var indent = string.Concat(line[..Math.Min(column, leading)]);
        var rest = line[column..];
        var dropped = column >= leading ? string.Concat(rest[..LeadingWhitespace(rest)]).Length : 0;
        var offset = CaretOffset;
        Document!.Replace(offset, dropped, "\n" + indent);
        CaretOffset = offset + 1 + indent.Length;

        if (indent.Length > 0 && LineText(row + 1).Length == indent.Length)
        {
            var anchor = Document.CreateAnchor(Document.GetLineByNumber(row + 2).Offset);
            anchor.MovementType = AnchorMovementType.BeforeInsertion;
            _autoIndents.Add((anchor, indent.Length));
        }
    }

    private bool TrimAutoIndents(IEnumerable<Caret> carets)
    {
        if (_autoIndents.Count == 0) return false;
        var occupied = carets.Select(c => Clamp(c.Position).Y).ToHashSet();
        var trimmed = false;
        for (var i = _autoIndents.Count - 1; i >= 0; i--)
        {
            var (anchor, count) = _autoIndents[i];
            if (anchor.IsDeleted)
            {
                _autoIndents.RemoveAt(i);
                continue;
            }
            var line = Document!.GetLineByOffset(anchor.Offset);
            if (occupied.Contains(line.LineNumber - 1)) continue;
            _autoIndents.RemoveAt(i);
            var text = LineText(line.LineNumber - 1);
            if (anchor.Offset != line.Offset || text.Length != count || text.Any(c => c is not (' ' or '\t'))) continue;
            Document.Remove(line.Offset, line.Length);
            trimmed = true;
        }
        return trimmed;
    }

    /// <summary>Leaves one caret at <paramref name="position"/>, with nothing selected.</summary>
    internal void MoveCaret(Point position)
    {
        RemoveSecondaryCarets();
        ClearSelection();
        CaretOffset = OffsetOf(position);
        LeaveAutoIndents();
    }

    // A move outside an edit: the trim isn't an undo step of its own, so the recorded lines take it too.
    private void LeaveAutoIndents()
    {
        if (_editing || _visitingCarets || _autoIndents.Count == 0) return;
        _editing = true;
        bool trimmed;
        try
        {
            trimmed = TrimAutoIndents(Carets);
        }
        finally
        {
            _editing = false;
        }
        if (!trimmed) return;
        _recorded.Clear();
        _recorded.AddRange(_lines);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private static int LeadingWhitespace(string[] line)
    {
        var count = 0;
        while (count < line.Length && line[count] is " " or "\t")
            count++;
        return count;
    }
}
