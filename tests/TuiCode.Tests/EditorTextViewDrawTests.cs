using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

// EditorTextView replaces TextView's draw loop, so it must paint exactly what TextView does.
public class EditorTextViewDrawTests : StaticConfigurationTest
{
    private const string Text =
        "plain ascii that runs past the right edge\n" +
        "\ttab\tseparated\tvalues\n" +
        "a中文b wide 中 glyphs 中文字符\n" +
        "\n" +
        "short\n" +
        "last";

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);

    public EditorTextViewDrawTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Theory]
    [InlineData("top")]
    [InlineData("scrolled right onto a wide glyph")]
    [InlineData("scrolled right past a tab")]
    [InlineData("scrolled down past the end")]
    [InlineData("selection")]
    [InlineData("overwrite cursor")]
    [InlineData("read-only")]
    public void Renders_exactly_like_TextView(string scenario)
    {
        var expected = Render(Configure(new TextView(), scenario));
        var actual = Render(Configure(new EditorTextView(), scenario));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Highlighted_cells_use_the_highlight_attribute()
    {
        var view = Configure(new EditorTextView(), "top");
        view.Highlights[4] = [(1, 3)];

        Render(view);

        Assert.Equal(
            ["Editable", "Highlight", "Highlight", "Editable"],
            Enumerable.Range(0, 4).Select(col => RoleAt(view, 4, col)));
    }

    [Fact]
    public void Selection_paints_over_a_highlight()
    {
        var view = Configure(new EditorTextView(), "top");
        view.Highlights[4] = [(0, 5)];
        view.SelectionStartRow = 4;
        view.SelectionStartColumn = 0;
        view.InsertionPoint = new System.Drawing.Point(2, 4);

        Render(view);

        Assert.Equal(
            ["Active", "Active", "Highlight"],
            Enumerable.Range(0, 3).Select(col => RoleAt(view, 4, col)));
    }

    [Fact]
    public void Syntax_colours_change_only_the_foreground()
    {
        var view = Configure(SyntaxView(), "top", "int x; // note");

        Render(view);

        var editable = view.GetAttributeForRole(VisualRole.Editable);
        Assert.Equal(editable with { Foreground = Color.Parse(DarkKeyword) }, AttributeAt(view, 0, 0));
        Assert.Equal(editable, AttributeAt(view, 0, 5));
        Assert.Equal(editable with { Foreground = Color.Parse(DarkComment) }, AttributeAt(view, 0, 7));
    }

    [Fact]
    public void Syntax_colours_follow_characters_rather_than_cells()
    {
        // The emoji is one model cell but two UTF-16 chars (and two screen columns); tokens after it must not shift.
        var view = Configure(SyntaxView(), "top", "s = \"😀\"; int n;");

        Render(view);

        Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(view, 0, 10).Foreground);
    }

    [Fact]
    public void Syntax_colours_are_right_when_scrolled_horizontally()
    {
        var view = Configure(SyntaxView(), "top", "string s = \"a string wider than the view\";");
        view.Viewport = view.Viewport with { X = 11 };

        Render(view);

        Assert.Equal("\"", _app.Driver!.Contents![0, 0].Grapheme);
        Assert.Equal(Color.Parse(DarkString), AttributeAt(view, 0, 0).Foreground);
    }

    [Fact]
    public void Find_highlights_paint_over_syntax_colours()
    {
        var view = Configure(SyntaxView(), "top", "int x;");
        view.Highlights[0] = [(0, 3)];

        Render(view);

        Assert.Equal("Highlight", RoleAt(view, 0, 0));
    }

    [Fact]
    public void An_unrecognised_file_type_is_drawn_plain()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/notes.unknown", new MockFileData("int x;"));
        using var tab = new EditorTab(fs.FileInfo.New("/work/notes.unknown"), new SyntaxHighlighter(GrammarBundle.Load()));

        Assert.Null(TextViewOf(tab).Syntax);
    }

    [Fact]
    public void A_file_is_coloured_by_its_extension()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/Program.cs", new MockFileData("int x;"));
        using var tab = new EditorTab(fs.FileInfo.New("/work/Program.cs"), new SyntaxHighlighter(GrammarBundle.Load()));

        Assert.NotNull(TextViewOf(tab).Syntax);
    }

    private const string DarkKeyword = "#569CD6";
    private const string DarkComment = "#6A9955";
    private const string DarkString = "#CE9178";

    private static EditorTextView SyntaxView()
    {
        var highlighter = new SyntaxHighlighter(GrammarBundle.Load());
        return new EditorTextView { Syntax = highlighter.CreateCache(highlighter.LanguageById("csharp")) };
    }

    private static EditorTextView TextViewOf(EditorTab tab) => tab.SubViews.OfType<EditorTextView>().Single();

    private Attribute AttributeAt(View view, int row, int col) => _app.Driver!.Contents![row - view.Viewport.Y, col].Attribute!.Value;

    private T Configure<T>(T view, string scenario, string text = Text) where T : TextView
    {
        view.App = _app;
        view.Width = 20;
        view.Height = 5;
        view.Text = text;
        view.BeginInit();
        view.EndInit();
        view.Layout();

        switch (scenario)
        {
            case "scrolled right onto a wide glyph":
                view.Viewport = view.Viewport with { X = 2 };
                break;
            case "scrolled right past a tab":
                view.Viewport = view.Viewport with { X = 5 };
                break;
            case "scrolled down past the end":
                view.Viewport = view.Viewport with { Y = 3 };
                break;
            case "selection":
                view.SelectionStartRow = 0;
                view.SelectionStartColumn = 6;
                view.InsertionPoint = new System.Drawing.Point(4, 2);
                break;
            case "overwrite cursor":
                view.SetFocus();
                view.Used = false;
                view.InsertionPoint = new System.Drawing.Point(3, 1);
                break;
            case "read-only":
                view.ReadOnly = true;
                break;
        }
        return view;
    }

    private string Render(View view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();

        var sb = new StringBuilder();
        for (var row = 0; row < view.Frame.Height; row++)
        {
            for (var col = 0; col < view.Frame.Width; col++)
                sb.Append(driver.Contents![row, col].Grapheme).Append(driver.Contents[row, col].Attribute).Append('|');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string RoleAt(View view, int row, int col)
    {
        var attribute = _app.Driver!.Contents![row - view.Viewport.Y, col].Attribute;
        return new[] { VisualRole.Highlight, VisualRole.Active, VisualRole.Editable }
            .First(role => view.GetAttributeForRole(role) == attribute).ToString();
    }
}
