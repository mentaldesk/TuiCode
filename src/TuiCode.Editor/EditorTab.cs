namespace TuiCode.Editor;

public sealed class EditorTab : FrameView
{
    private readonly TextView _textView;
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

        _textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = file.FileSystem.File.ReadAllText(file.FullName)
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
        var content = _textView.Text;
        if (content.Length > 0 && !content.EndsWith('\n'))
            content += '\n';
        File.FileSystem.File.WriteAllText(File.FullName, content);
        if (_dirty)
        {
            _dirty = false;
            UpdateTitle();
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
        Saved?.Invoke(this, EventArgs.Empty);
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
