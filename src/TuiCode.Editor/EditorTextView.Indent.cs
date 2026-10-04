using Terminal.Gui.Text;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Tab and Shift+Tab (#14, #386): TG's own only insert and remove tab characters.
internal sealed partial class EditorTextView
{
    public bool InsertSpaces { get; set; }

    private bool Indent()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        if (!InsertSpaces)
        {
            InsertText("\t");
            return true;
        }
        if (IsSelecting && (SelectionStartRow != CurrentRow || SelectionStartColumn != CurrentColumn))
            DeleteCharLeft();
        InsertText(new string(' ', TabWidth - DisplayColumn() % TabWidth));
        return true;
    }

    /// <summary>Indents every line a multi-line selection touches, or else inserts an indent at every caret, as one undo step.</summary>
    public bool IndentLines()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        var carets = Carets;
        if (carets.Any(c => c.Start.Y != c.End.Y))
            ShiftLines(carets, outdent: false);
        else if (_secondary.Count == 0)
            EditAtPrimary(() => Indent());
        else
            AtEachCaret(_ => Indent());
        return true;
    }

    /// <summary>Outdents every line under a caret or selection by up to one level, as one undo step.</summary>
    public bool OutdentLines()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        ShiftLines(Carets, outdent: true);
        return true;
    }

    private void ShiftLines(Caret[] carets, bool outdent)
    {
        var unit = Cell.ToCellList(InsertSpaces ? new string(' ', TabWidth) : "\t");
        EditLines(carets, blocks =>
        {
            var shifted = new Dictionary<int, (int At, int By)>();
            foreach (var (first, last) in blocks)
            {
                for (var row = first; row <= last; row++)
                {
                    var line = GetLine(row);
                    if (!outdent)
                    {
                        if (line.Count == 0) continue;
                        line.InsertRange(0, unit);
                        shifted[row] = (0, unit.Count);
                        continue;
                    }
                    var leading = LeadingWhitespace(line);
                    var kept = OutdentedLength(line, leading);
                    if (kept == leading) continue;
                    line.RemoveRange(kept, leading - kept);
                    shifted[row] = (kept, kept - leading);
                }
            }
            return shifted;
        });
    }

    /// <summary>
    /// Edits the lines under <paramref name="carets"/> as one undo step. The edit reports each row it changed as the column the
    /// change starts at and the cells it added (or removed, if negative), so carets after that column follow their text.
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
                    ? point with { X = Math.Clamp(point.X + shift.By, shift.At, GetLine(point.Y).Count) }
                    : point;
        });
    }

    // Trims the leading whitespace from its end back to the previous tab stop, so mixed tabs and spaces stay as they were.
    private int OutdentedLength(List<Cell> line, int leading)
    {
        var width = ColumnsBefore(line, leading);
        var target = Math.Max(width % TabWidth == 0 ? width - TabWidth : width - width % TabWidth, 0);
        var kept = leading;
        while (kept > 0 && ColumnsBefore(line, kept) > target)
            kept--;
        return kept;
    }

    private int DisplayColumn()
    {
        var line = GetLine(CurrentRow);
        var width = 0;
        for (var i = 0; i < CurrentColumn && i < line.Count; i++)
        {
            var grapheme = line[i].Grapheme;
            width += grapheme == "\t" ? TabWidth - width % TabWidth : Math.Max(grapheme.GetColumns(), 1);
        }
        return width;
    }
}

// Enter keeps the indentation of the line it was pressed on (#385).
internal sealed partial class EditorTextView
{
    // Whitespace Enter put on an otherwise empty line, with its length then; it's trimmed once no caret is on that line.
    private readonly List<(List<Cell> Line, int Count)> _autoIndents = [];

    private void NewLineKeepingIndent(Action newLine)
    {
        if (ReadOnly)
        {
            newLine();
            return;
        }
        if (IsSelecting && (SelectionStartRow != CurrentRow || SelectionStartColumn != CurrentColumn))
            DeleteCharLeft();
        IsSelecting = false;

        var row = CurrentRow;
        var line = GetLine(row);
        var column = Math.Min(CurrentColumn, line.Count);
        var leading = LeadingWhitespace(line);
        var indent = line[..Math.Min(column, leading)];
        newLine();
        if (CurrentRow != row + 1) return;

        var next = GetLine(CurrentRow);
        if (column >= leading) next.RemoveRange(0, LeadingWhitespace(next));
        next.InsertRange(0, indent);
        if (indent.Count > 0 && next.Count == indent.Count) _autoIndents.Add((next, indent.Count));
        if (_visitingCarets)
            SetCurrentColumn(this, indent.Count);
        else
            InsertionPoint = new Point(indent.Count, CurrentRow);
    }

    private bool TrimAutoIndents(IEnumerable<Caret> carets)
    {
        if (_autoIndents.Count == 0) return false;
        var occupied = carets.Select(c => GetLine(Clamp(c.Position).Y)).ToHashSet(ReferenceEqualityComparer.Instance);
        var trimmed = false;
        for (var i = _autoIndents.Count - 1; i >= 0; i--)
        {
            var (line, count) = _autoIndents[i];
            if (occupied.Contains(line)) continue;
            _autoIndents.RemoveAt(i);
            if (line.Count != count || LeadingWhitespace(line) != count) continue;
            line.Clear();
            trimmed = true;
        }
        return trimmed;
    }

    /// <summary>Leaves one caret at <paramref name="position"/>, with nothing selected.</summary>
    internal void MoveCaret(Point position)
    {
        RemoveSecondaryCarets();
        IsSelecting = false;
        InsertionPoint = position;
        // TG doesn't always report a move made through InsertionPoint.
        LeaveAutoIndents();
    }

    protected override void OnUnwrappedCursorPositionChanged(Point position) => LeaveAutoIndents();

    // A move outside an edit: the trim isn't an undo step of its own, so the recorded lines take it too.
    private void LeaveAutoIndents()
    {
        if (_holdContentsChanged || _visitingCarets || !TrimAutoIndents(Carets)) return;
        _recorded.Clear();
        _recorded.AddRange(Snapshot.Refresh(GetAllLines()));
        CachedMaxWidth(ModelField.GetValue(this)!) = -1;
        RaiseContentsChanged();
    }

    private static int LeadingWhitespace(List<Cell> line)
    {
        var count = 0;
        while (count < line.Count && line[count].Grapheme is " " or "\t")
            count++;
        return count;
    }
}
