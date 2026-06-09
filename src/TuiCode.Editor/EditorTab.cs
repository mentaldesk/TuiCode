namespace TuiCode.Editor;

public sealed class EditorTab : FrameView
{
    private readonly TextView _textView;
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
            MarkDirty();
        }
    }

    public int CursorRow => _textView.CurrentRow;
    public int CursorColumn => _textView.CurrentColumn;

    public event EventHandler? DirtyChanged;
    public event EventHandler? Saved;

    public EditorTab(IFileInfo file)
    {
        File = file;
        BorderStyle = LineStyle.None;

        var initial = file.FileSystem.File.ReadAllText(file.FullName);
        _eol = DetectEol(initial);

        _textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = initial
        };
        // Subscribe AFTER setting initial text so the load doesn't mark dirty.
        _textView.ContentsChanged += (_, _) => MarkDirty();
        Add(_textView);

        UpdateTitle();
    }

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

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle() => Title = _dirty ? $"● {File.Name}" : File.Name;
}
