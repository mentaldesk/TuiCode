using System.Drawing;

namespace TuiCode.Editor;

/// <summary>
/// A read-only Markdown document that isn't on disk (#185), like a PR's Overview. TG's
/// <see cref="Markdown"/> view renders it, wrapping to the tab's width, and brings its own
/// scrolling, selection and copy; <see cref="File"/> only names it.
/// </summary>
public sealed class DocumentTab : FrameView
{
    private readonly Markdown _markdown;

    public DocumentTab(IFileInfo file, string content, string? title = null)
    {
        File = file;
        Title = title ?? file.Name;
        BorderStyle = LineStyle.None;
        CanFocus = true;

        _markdown = new ScrolledMarkdown
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

    /// <summary>
    /// Reports what it drew from the viewport, not the content origin: TG 2.1.0's <see cref="Markdown"/> uses
    /// the latter, which once scrolled sits above the view and clips the neighbouring tab's header away.
    /// </summary>
    private sealed class ScrolledMarkdown : Markdown
    {
        protected override bool OnDrawingContent(DrawContext? context)
        {
            base.OnDrawingContent(null);
            context?.AddDrawnRectangle(ViewportToScreen(new Rectangle(Point.Empty, Viewport.Size)));
            return true;
        }
    }
}
