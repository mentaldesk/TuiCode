namespace TuiCode.Workbench.Parts;

public sealed class EditorPart : FrameView
{
    private static readonly Key CtrlS =
        Key.TryParse("Ctrl+S", out var k) ? k : Key.Empty;

    private readonly TextView _textView;

    public event EventHandler<IFileInfo>? FileSaved;

    public IFileInfo? CurrentFile { get; private set; }

    public string Content
    {
        get => _textView.Text;
        set => _textView.Text = value;
    }

    public bool IsDirty => _textView.IsDirty;

    public EditorPart()
    {
        Title = "Editor";
        BorderStyle = LineStyle.Single;

        _textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        _textView.KeyDown += OnKeyDown;
        Add(_textView);
    }

    public void Open(IFileInfo file)
    {
        CurrentFile = file;
        _textView.Text = file.FileSystem.File.ReadAllText(file.FullName);
        _textView.ReadOnly = false;
        Title = $"Editor — {file.Name}";
    }

    public void Save()
    {
        if (CurrentFile is null) return;
        CurrentFile.FileSystem.File.WriteAllText(CurrentFile.FullName, _textView.Text);
        FileSaved?.Invoke(this, CurrentFile);
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (key == CtrlS)
        {
            Save();
            key.Handled = true;
        }
    }
}
