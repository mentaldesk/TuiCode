using TuiCode.Abstractions;

namespace TuiCode.Editor;

public sealed class EditorTab : FrameView
{
    private readonly EditorTextView _textView;
    private readonly EditorGutter _gutter;
    private View? _header;
    private readonly string _eol;
    private bool _dirty;

    public IFileInfo File { get; }
    public bool IsDirty => _dirty;

    public string Content
    {
        get => _textView.Text;
        set
        {
            _textView.Text = value;
            OnEdited();
        }
    }

    public int CursorRow => _textView.CurrentRow;
    public int CursorColumn => _textView.CurrentColumn;

    public event EventHandler? DirtyChanged;

    /// <summary>Raised on every edit, whether typed, pasted, replaced or set via <see cref="Content"/>.</summary>
    public event EventHandler? ContentChanged;
    public event EventHandler? Saved;

    /// <summary>
    /// Raised whenever the insertion point moves, carrying the position in the file's own
    /// (unwrapped) model coordinates — the raw source for cursor-location history (#35).
    /// </summary>
    public event EventHandler<(int Row, int Column)>? CursorMoved;

    public EditorTab(IFileInfo file)
    {
        File = file;
        BorderStyle = LineStyle.None;

        var initial = file.FileSystem.File.ReadAllText(file.FullName);
        _eol = DetectEol(initial);

        _textView = new EditorTextView
        {
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = initial
        };
        _gutter = new EditorGutter(_textView) { X = 0, Y = 0, Height = Dim.Fill() };
        _textView.X = Pos.Right(_gutter);
        // Subscribe AFTER setting initial text so the load doesn't mark dirty.
        _textView.ContentsChanged += (_, _) => OnEdited();
        // Point is (X=column, Y=row). Re-expose in (row, column) order to match the rest of the editor API.
        _textView.UnwrappedCursorPositionChanged += (_, point) =>
            CursorMoved?.Invoke(this, (point.Y, point.X));
        Add(_gutter, _textView);

        UpdateTitle();
    }

    public bool GutterVisible
    {
        get => _gutter.Visible;
        set
        {
            _gutter.Visible = value;
            _textView.X = value ? Pos.Right(_gutter) : 0;
            SetNeedsLayout();
        }
    }

    internal IReadOnlyList<LineChange> LineChanges => _gutter.Changes;

    public bool FocusContent() => _textView.SetFocus();
    public bool ContentHasFocus => _textView.HasFocus;

    /// <summary>
    /// Move the cursor to the given (zero-based) row/column. Out-of-range values clamp
    /// to the nearest valid position.
    /// </summary>
    public void MoveCursor(int row, int col)
    {
        if (row < 0) row = 0;
        if (col < 0) col = 0;

        var text = _textView.Text ?? string.Empty;

        var currentRow = 0;
        var lineStart = 0;
        for (var i = 0; i < text.Length && currentRow < row; i++)
        {
            if (text[i] == '\n')
            {
                currentRow++;
                lineStart = i + 1;
            }
        }

        // Requested row past the end? Stay on the last line we reached.
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0) lineEnd = text.Length;
        var lineLen = lineEnd - lineStart;
        if (col > lineLen) col = lineLen;

        _textView.InsertionPoint = new System.Drawing.Point(col, currentRow);
    }

    /// <summary>
    /// The buffer's lines as the editor models them (terminators excluded). Columns in a
    /// <see cref="TextMatch"/> computed against these line up with <see cref="Select"/> / <see cref="Replace"/>.
    /// </summary>
    public IReadOnlyList<string> Lines => _textView.LineStrings;

    public string SelectedText => _textView.SelectedText;

    /// <summary>
    /// Start of the selection, or the cursor when nothing is selected — with the column in UTF-16 chars,
    /// comparable with <see cref="TextMatch"/> positions.
    /// </summary>
    public (int Row, int Column) SelectionOrigin
    {
        get
        {
            var (row, col) = (_textView.CurrentRow, _textView.CurrentColumn);
            if (_textView.IsSelecting)
            {
                var (startRow, startCol) = (_textView.SelectionStartRow, _textView.SelectionStartColumn);
                if (startRow < row || (startRow == row && startCol < col))
                    (row, col) = (startRow, startCol);
            }
            return (row, ToCharColumn(row, col));
        }
    }

    /// <summary>
    /// Dock <paramref name="header"/> (e.g. the find bar) above the text, pushing the text down; null removes it.
    /// The header is not disposed on removal — its owner is.
    /// </summary>
    public void SetHeader(View? header)
    {
        if (ReferenceEquals(_header, header)) return;
        if (_header is not null) Remove(_header);
        _header = header;
        if (header is not null)
        {
            header.X = 0;
            header.Y = 0;
            header.Width = Dim.Fill();
            Add(header);
        }
        _textView.Y = _gutter.Y = header is null ? 0 : Pos.Bottom(header);
        SetNeedsLayout();
    }

    /// <summary>Paint every match with the highlight colour (find-in-file). An empty set clears it.</summary>
    public void SetHighlights(IEnumerable<TextMatch> matches)
    {
        _textView.Highlights.Clear();
        foreach (var m in matches)
        {
            if (!_textView.Highlights.TryGetValue(m.Row, out var ranges))
                _textView.Highlights[m.Row] = ranges = [];
            ranges.Add((ToCellColumn(m.Row, m.Column), ToCellColumn(m.Row, m.End)));
        }
        _textView.SetNeedsDraw();
    }

    /// <summary>Select <paramref name="match"/>, leaving the cursor at its end and scrolling it into view.</summary>
    public void Select(TextMatch match)
    {
        var start = ToCellColumn(match.Row, match.Column);
        var end = ToCellColumn(match.Row, match.End);
        _textView.InsertionPoint = new System.Drawing.Point(start, match.Row);
        // Row before column: the column setter clamps against the selection-start row's line.
        _textView.SelectionStartRow = match.Row;
        _textView.SelectionStartColumn = start;
        _textView.InsertionPoint = new System.Drawing.Point(end, match.Row);
    }

    public void ClearSelection()
    {
        _textView.IsSelecting = false;
        _textView.SetNeedsDraw();
    }

    /// <summary>Replace the text covered by <paramref name="match"/>; goes through TextView editing so it's undoable.</summary>
    public void Replace(TextMatch match, string replacement)
    {
        Select(match);
        if (match.Length > 0) _textView.DeleteCharLeft();
        if (replacement.Length > 0) _textView.InsertText(replacement);
        _textView.IsSelecting = false;
    }

    private int ToCharColumn(int row, int cellColumn)
    {
        var line = _textView.GetLine(row);
        var chars = 0;
        for (var i = 0; i < Math.Min(cellColumn, line.Count); i++)
            chars += line[i].Grapheme.Length;
        return chars;
    }

    // TextMatch columns count UTF-16 chars; the TextView model counts grapheme cells.
    private int ToCellColumn(int row, int charColumn)
    {
        var line = _textView.GetLine(row);
        var chars = 0;
        for (var i = 0; i < line.Count; i++)
        {
            if (chars >= charColumn) return i;
            chars += line[i].Grapheme.Length;
        }
        return line.Count;
    }

    public void Save()
    {
        // TextView.Text joins its lines with Environment.NewLine, so on Windows the
        // buffer comes back CRLF regardless of the file's real endings. Re-emit using
        // the EOL we detected on load so a file's line-ending style round-trips
        // unchanged on every OS (matches VS Code's preserve-on-save behaviour).
        var content = Normalize(_textView.Text, _eol);
        if (content.Length > 0 && !content.EndsWith(_eol, StringComparison.Ordinal))
            content += _eol;
        File.FileSystem.File.WriteAllText(File.FullName, content);
        _gutter.ResetBaseline();
        if (_dirty)
        {
            _dirty = false;
            UpdateTitle();
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
        Saved?.Invoke(this, EventArgs.Empty);
    }

    // The file's line-ending style, fixed on load. An empty buffer is a new/blank file: there's
    // nothing to preserve, so it takes the OS default (CRLF on Windows, LF elsewhere) — what VS
    // Code does for new files, and what Windows users expect. A non-empty file keeps its own
    // style instead: the first line break wins (CRLF vs LF), or LF if it has none (single line).
    private static string DetectEol(string text)
    {
        if (text.Length == 0) return Environment.NewLine;
        var i = text.IndexOf('\n');
        if (i < 0) return "\n";
        return i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n";
    }

    // Collapse whatever endings TextView produced to LF, then re-emit with the target EOL.
    private static string Normalize(string text, string eol)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return eol == "\n" ? text : text.Replace("\n", eol);
    }

    // The header (find bar) belongs to its controller, which outlives this tab — don't take it down with us.
    protected override void Dispose(bool disposing)
    {
        if (disposing) SetHeader(null);
        base.Dispose(disposing);
    }

    private void OnEdited()
    {
        MarkDirty();
        _gutter.OnContentChanged();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle()
    {
        Title = _dirty ? $"● {File.Name}" : File.Name;
        // TG redraws the tab header from Title only on layout, and positions headers from a cached width first.
        if (Border.View is BorderView { TitleView: ITitleView header }) header.MeasuredTabLength = 0;
        SetNeedsLayout();
    }
}

/// <summary>
/// TextView that paints find-in-file match highlights (#33) under the normal text colour, and raises
/// <see cref="TextView.ContentsChanged"/> only for edits that change the text.
/// </summary>
internal sealed class EditorTextView : TextView
{
    // TG 2.1.0 raises ContentsChanged for these on some paths only, and on some no-op paths too.
    private static readonly Command[] UnreliablyReportedEdits =
        [Command.CutToEndOfLine, Command.CutToStartOfLine, Command.KillWordLeft, Command.KillWordRight];

    private bool _holdContentsChanged;

    /// <summary>Row → half-open [start, end) cell-column ranges to highlight.</summary>
    public Dictionary<int, List<(int Start, int End)>> Highlights { get; } = new();

    public IReadOnlyList<string> LineStrings => GetAllLines().Select(Cell.ToString).ToArray();

    public override void OnContentsChanged()
    {
        if (!_holdContentsChanged) base.OnContentsChanged();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (base.OnKeyDown(key)) return true;
        if (!KeyBindings.TryGet(key, out var binding)) return false;

        var unreliable = binding.Commands.Any(UnreliablyReportedEdits.Contains);
        if (!unreliable && !IsNoOpDelete(binding.Commands)) return false;

        var before = unreliable ? Text : null;
        _holdContentsChanged = true;
        try
        {
            InvokeCommands(binding.Commands, binding);
        }
        finally
        {
            _holdContentsChanged = false;
        }
        if (unreliable && Text != before) OnContentsChanged();
        // Already invoked: returning false would let TG invoke the bound commands a second time.
        return true;
    }

    // TG 2.1.0 raises ContentsChanged for these even with nothing to delete. Position-only, so it's cheap per keystroke.
    private bool IsNoOpDelete(Command[] commands) =>
        !IsSelecting && commands switch
        {
            [Command.DeleteCharLeft] => CurrentRow == 0 && CurrentColumn == 0,
            [Command.DeleteCharRight] => CurrentRow == Lines - 1 && CurrentColumn == GetLine(CurrentRow).Count,
            _ => false,
        };

    protected override void OnDrawNormalColor(List<Cell> line, int idxCol, int idxRow)
    {
        base.OnDrawNormalColor(line, idxCol, idxRow);
        // We never enable WordWrap, so draw coordinates are model coordinates.
        if (Highlights.TryGetValue(idxRow, out var ranges) && ranges.Exists(r => idxCol >= r.Start && idxCol < r.End))
            SetAttributeForRole(VisualRole.Highlight);
    }
}
