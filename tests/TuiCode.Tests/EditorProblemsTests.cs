using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// The language server's problems drawn in an editor tab (#687). Renders through a TG driver — serialised (#77).
public class EditorProblemsTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = Midnight();
    private static readonly Color ErrorColour = Color.Parse(Syntax.EditorColors[ProblemColours.ErrorKey]);
    private static readonly Color WarningColour = Color.Parse(Syntax.EditorColors[ProblemColours.WarningKey]);

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorProblemsTests() => _app.Driver!.SetScreenSize(80, 30);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_problem_is_underlined_in_its_colour_and_nothing_around_it_is()
    {
        using var tab = Tab("var x = nam;\nvar y = 1;\n");

        tab.Problems = [new Problem(0, 8, 0, 11, ProblemSeverity.Error, "CS0103", "")];
        Render(tab);

        Assert.All([8, 9, 10], col => Assert.Equal((ErrorColour, true), Underline(AttributeAt(tab, 0, col))));
        Assert.False(Underline(AttributeAt(tab, 0, 7)).Underlined);
        Assert.False(Underline(AttributeAt(tab, 0, 11)).Underlined);
    }

    [Fact]
    public void A_problem_after_an_emoji_is_underlined_where_the_server_counts_it()
    {
        using var tab = Tab("\"😀\" + nam\n");

        tab.Problems = [new Problem(0, 7, 0, 10, ProblemSeverity.Warning, null, "")];
        Render(tab);

        // The emoji is two chars to the server and two cells on screen, so "nam" starts at cell 7 here too.
        Assert.All([7, 8, 9], col => Assert.Equal((WarningColour, true), Underline(AttributeAt(tab, 0, col))));
        Assert.False(Underline(AttributeAt(tab, 0, 6)).Underlined);
    }

    [Fact]
    public void A_problem_over_several_lines_is_underlined_on_each()
    {
        using var tab = Tab("alpha\nbravo\ncharlie\n");

        tab.Problems = [new Problem(0, 3, 2, 2, ProblemSeverity.Error, null, "")];
        Render(tab);

        Assert.True(Underline(AttributeAt(tab, 0, 4)).Underlined);
        Assert.True(Underline(AttributeAt(tab, 1, 0)).Underlined);
        Assert.True(Underline(AttributeAt(tab, 2, 1)).Underlined);
        Assert.False(Underline(AttributeAt(tab, 2, 2)).Underlined);
    }

    [Fact]
    public void The_line_number_takes_the_worst_problems_colour_and_the_gutter_stays_as_wide()
    {
        using var tab = Tab("alpha\nbravo\ncharlie\n");
        var width = tab.Code.Frame.X;

        tab.Problems =
        [
            new Problem(0, 0, 0, 1, ProblemSeverity.Warning, null, ""),
            new Problem(0, 2, 0, 3, ProblemSeverity.Error, null, ""),
            new Problem(1, 0, 1, 1, ProblemSeverity.Warning, null, ""),
        ];
        Render(tab);

        Assert.Equal(ErrorColour, NumberAt(tab, 0).Foreground);
        Assert.Equal(WarningColour, NumberAt(tab, 1).Foreground);
        Assert.NotEqual(ErrorColour, NumberAt(tab, 2).Foreground);
        Assert.NotEqual(WarningColour, NumberAt(tab, 2).Foreground);
        Assert.Equal(width, tab.Code.Frame.X);
    }

    [Fact]
    public void Clearing_the_problems_removes_the_underline_and_the_line_numbers_colour()
    {
        using var tab = Tab("var x = nam;\n");
        tab.MoveCursor(1, 0);
        tab.Problems = [new Problem(0, 8, 0, 11, ProblemSeverity.Error, null, "")];
        Render(tab);

        tab.Problems = [];
        Render(tab);

        Assert.False(Underline(AttributeAt(tab, 0, 9)).Underlined);
        Assert.NotEqual(ErrorColour, NumberAt(tab, 0).Foreground);
    }

    [Fact]
    public void With_the_line_numbers_hidden_the_underline_still_shows()
    {
        using var tab = Tab("var x = nam;\n");
        tab.GutterVisible = false;

        tab.Problems = [new Problem(0, 8, 0, 11, ProblemSeverity.Error, null, "")];
        Render(tab);

        Assert.Equal((ErrorColour, true), Underline(AttributeAt(tab, 0, 9)));
    }

    [Fact]
    public void A_problem_scrolled_out_of_view_and_back_keeps_its_underline()
    {
        using var tab = Tab(string.Concat(Enumerable.Repeat("line\n", 100)) + "var x = nam;\n");
        tab.Problems = [new Problem(100, 8, 100, 11, ProblemSeverity.Error, null, "")];

        tab.MoveCursor(100, 0);
        Render(tab);
        tab.MoveCursor(0, 0);
        Render(tab);
        tab.MoveCursor(100, 0);
        Render(tab);

        var row = 100 - tab.TopRow;
        Assert.Equal((ErrorColour, true), Underline(AttributeAt(tab, row, 9)));
    }

    [Fact]
    public void The_caret_on_a_problem_finds_it_worst_first()
    {
        using var tab = Tab("var x = nam;\n");
        var error = new Problem(0, 8, 0, 11, ProblemSeverity.Error, null, "");
        var warning = new Problem(0, 4, 0, 11, ProblemSeverity.Warning, null, "");
        tab.Problems = [warning, error];

        tab.MoveCursor(0, 9);
        var on = tab.ProblemsAtCursor();
        tab.MoveCursor(0, 2);

        Assert.Equal([error, warning], on);
        Assert.Empty(tab.ProblemsAtCursor());
    }

    private static (Color Foreground, bool Underlined) Underline(Attribute attribute) =>
        (attribute.Foreground, attribute.Style.HasFlag(TextStyle.Underline));

    private static SyntaxHighlighter Midnight()
    {
        var highlighter = new SyntaxHighlighter(GrammarBundle.Load());
        highlighter.UseTheme("midnight.json");
        return highlighter;
    }

    private EditorTab Tab(string content)
    {
        _fs.AddFile("/work/a.cs", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New("/work/a.cs"), Syntax) { App = _app, Width = 60, Height = 20 };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        Render(tab);
        return tab;
    }

    private Attribute AttributeAt(EditorTab tab, int row, int col)
    {
        var screen = tab.Code.ViewportToScreen(new Point(col, row));
        return _app.Driver!.Contents![screen.Y, screen.X].Attribute!.Value;
    }

    // The number's last digit, left of the space, fold marker and change marker.
    private Attribute NumberAt(EditorTab tab, int row)
    {
        var screen = tab.Code.ViewportToScreen(new Point(0, row));
        return _app.Driver!.Contents![screen.Y, screen.X - 4].Attribute!.Value;
    }

    private void Render(View view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.Layout();
        view.SetNeedsDraw();
        view.Draw();
    }
}
