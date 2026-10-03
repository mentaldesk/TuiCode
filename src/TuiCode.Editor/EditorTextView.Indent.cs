using Terminal.Gui.Text;
using Point = System.Drawing.Point;

namespace TuiCode.Editor;

// Tab and Shift+Tab (#14): TG's own only insert and remove tab characters.
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

    // Removes a tab, or the spaces back to the previous tab stop, just left of the caret.
    private bool Outdent()
    {
        if (!TabKeyAddsTab || ReadOnly) return false;
        if (IsSelecting || CurrentColumn == 0) return true;

        var line = GetLine(CurrentRow);
        var col = Math.Min(CurrentColumn, line.Count);
        if (line[col - 1].Grapheme == "\t")
        {
            DeleteCharLeft();
            return true;
        }

        var toStop = DisplayColumn() % TabWidth is var partial and > 0 ? partial : TabWidth;
        var spaces = 0;
        while (spaces < toStop && col - spaces > 0 && line[col - spaces - 1].Grapheme == " ")
            spaces++;
        for (var i = 0; i < spaces; i++)
            DeleteCharLeft();
        return true;
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
