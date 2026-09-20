namespace TuiCode.Editor;

/// <summary>
/// A read-only Markdown document that isn't on disk (#185), like a PR's Overview. TG's
/// <see cref="Markdown"/> view renders it, wrapping to the tab's width, and brings its own
/// scrolling, selection and copy; <see cref="File"/> only names it.
/// </summary>
public sealed class DocumentTab : FrameView
{
    private readonly Markdown _markdown;

    public DocumentTab(IFileInfo file, string content)
    {
        File = file;
        Title = file.Name;
        BorderStyle = LineStyle.None;
        CanFocus = true;

        _markdown = new Markdown
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Text = content,
        };
        Add(_markdown);
    }

    public IFileInfo File { get; }

    public string Content => _markdown.Text;

    /// <summary>The rows the document renders to at the current width, wrapping included.</summary>
    internal int RenderedLines => _markdown.LineCount;

    public bool FocusContent() => _markdown.SetFocus();
}
