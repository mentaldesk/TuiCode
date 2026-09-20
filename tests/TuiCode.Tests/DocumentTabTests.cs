using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class DocumentTabTests : StaticConfigurationTest
{
    private const string Paragraph =
        "# #183 Review tab shows the PR\n\n" +
        "A paragraph long enough that a narrow sidebar-width tab has to wrap it over several rows " +
        "instead of cutting it off after the first sentence, which is what reading a PR description needs.\n";

    private static readonly string LongDocument =
        Paragraph + string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Another paragraph, number {i}, so the document has somewhere to scroll."));

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public DocumentTabTests() => _app.Driver!.SetScreenSize(60, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_long_paragraph_wraps_to_the_tabs_width()
    {
        using var tab = Document(Paragraph);

        var wide = Rows(tab, 100);
        var narrow = Rows(tab, 30);

        Assert.True(narrow > wide, $"expected more rows at 30 columns than at 100; got {narrow} and {wide}");
    }

    [Fact]
    public void The_document_keeps_the_text_it_was_opened_with()
    {
        using var tab = Document(Paragraph);

        Assert.Equal(Paragraph, tab.Content);
    }

    [Fact]
    public void Scrolling_the_document_leaves_the_neighbouring_tabs_header_alone()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        using var group = new EditorGroup { App = _app, Width = Dim.Fill(), Height = Dim.Fill() };
        group.OpenOrFocus(fs.FileInfo.New("/work/a.txt"));
        var document = group.OpenDocument(fs.FileInfo.New("/work/#183 Overview"), LongDocument);
        Render(group);
        Assert.Contains("a.txt", Header());

        Scroll(document, 3);
        Render(group);

        Assert.Contains("a.txt", Header());
    }

    private static void Scroll(DocumentTab tab, int rows)
    {
        var markdown = tab.SubViews.OfType<Markdown>().Single();
        markdown.Viewport = markdown.Viewport with { Y = rows };
        Assert.Equal(rows, markdown.Viewport.Y);
    }

    private string Header()
    {
        var sb = new StringBuilder();
        var contents = _app.Driver!.Contents!;
        for (var col = 0; col < contents.GetLength(1); col++) sb.Append(contents[1, col].Grapheme);
        return sb.ToString();
    }

    private void Render(View view)
    {
        view.Layout();
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
    }

    private static int Rows(DocumentTab tab, int width)
    {
        tab.Layout(new System.Drawing.Size(width, 40));
        return tab.RenderedLines;
    }

    private static DocumentTab Document(string content)
    {
        var fs = new MockFileSystem();
        return new DocumentTab(fs.FileInfo.New("/work/#183 Overview"), content) { Width = Dim.Fill(), Height = Dim.Fill() };
    }
}
