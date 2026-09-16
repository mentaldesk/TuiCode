using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Editor;

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

    private T Configure<T>(T view, string scenario) where T : TextView
    {
        view.App = _app;
        view.Width = 20;
        view.Height = 5;
        view.Text = Text;
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
