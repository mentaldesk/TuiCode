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
