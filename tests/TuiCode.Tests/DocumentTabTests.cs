using Terminal.Gui.ViewBase;
using TuiCode.Editor;

namespace TuiCode.Tests;

public class DocumentTabTests : StaticConfigurationTest
{
    private const string Paragraph =
        "# #183 Review tab shows the PR\n\n" +
        "A paragraph long enough that a narrow sidebar-width tab has to wrap it over several rows " +
        "instead of cutting it off after the first sentence, which is what reading a PR description needs.\n";

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
