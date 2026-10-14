using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Bracket matching in an editor tab (#539). Renders through a TG driver — serialised (#77).
public class EditorBracketMatchTests : StaticConfigurationTest
{
    private static readonly SyntaxHighlighter Syntax = Midnight();

    private static readonly string Code = string.Join('\n',
    [
        "void Run()",
        "{",
        "    foreach (var item in items)",
        "    {",
        "        Handle(item);",
        "    }",
        "}",
    ]);

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorBracketMatchTests() => _app.Driver!.SetScreenSize(80, 30);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void The_bracket_at_the_cursor_and_its_partner_get_the_bracket_match_background()
    {
        using var tab = Tab("a.cs", Code);
        var editable = tab.Code.GetAttributeForRole(VisualRole.Editable);

        tab.MoveCursor(3, 4);
        Render(tab);

        var match = Color.Parse(Syntax.EditorColors["editorBracketMatch.background"]);
        Assert.Equal((new Point(4, 3), new Point(4, 5), false), tab.Code.BracketMatch);
        Assert.Equal(match, AttributeAt(tab, 3, 4).Background);
        Assert.Equal(match, AttributeAt(tab, 5, 4).Background);
        Assert.Equal(editable.Background, AttributeAt(tab, 1, 0).Background);
    }

    [Fact]
    public void Moving_the_cursor_away_removes_the_highlight()
    {
        using var tab = Tab("a.cs", Code);
        var editable = tab.Code.GetAttributeForRole(VisualRole.Editable);
        tab.MoveCursor(3, 4);
        Render(tab);

        tab.MoveCursor(4, 10);
        Render(tab);

        Assert.Null(tab.Code.BracketMatch);
        Assert.Equal(editable.Background, AttributeAt(tab, 3, 4).Background);
        Assert.Equal(editable.Background, AttributeAt(tab, 5, 4).Background);
    }

    [Fact]
    public void Just_after_a_closing_bracket_it_and_its_opener_are_highlighted()
    {
        using var tab = Tab("a.cs", Code);

        tab.MoveCursor(4, 20);
        Render(tab);

        Assert.Equal((new Point(19, 4), new Point(14, 4), false), tab.Code.BracketMatch);
    }

    [Fact]
    public void Typing_a_bracket_highlights_its_partner()
    {
        using var tab = Tab("a.cs", "f(x");
        tab.MoveCursor(0, 3);
        Render(tab);

        tab.Code.NewKeyDownEvent(new Key(')'));
        Render(tab);

        Assert.Equal("f(x)", tab.Lines[0]);
        Assert.Equal((new Point(3, 0), new Point(1, 0), false), tab.Code.BracketMatch);
    }

    [Fact]
    public void A_bracket_with_no_partner_is_drawn_in_the_error_colour()
    {
        using var themes = LoadBundledThemes();
        using var tab = Tab("a.cs", "{\n    f(x;\n}");

        tab.MoveCursor(1, 5);
        Render(tab);

        Assert.True(SchemeManager.TryGetScheme("Error", out var error));
        Assert.Equal((new Point(5, 1), null, true), tab.Code.BracketMatch);
        Assert.Equal(error.Active.Background, AttributeAt(tab, 1, 5).Background);
    }

    [Fact]
    public void A_bracket_in_a_string_is_not_highlighted()
    {
        using var tab = Tab("a.cs", "var s = \"(\";");

        tab.MoveCursor(0, 9);
        Render(tab);

        Assert.Null(tab.Code.BracketMatch);
    }

    [Fact]
    public void Going_to_the_matching_bracket_jumps_to_the_partner_and_back()
    {
        using var tab = Tab("a.cs", Code);
        tab.MoveCursor(1, 0);

        Assert.True(tab.GoToMatchingBracket());
        Assert.Equal((6, 0), (tab.CursorRow, tab.CursorColumn));

        Assert.True(tab.GoToMatchingBracket());
        Assert.Equal((1, 0), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Inside_a_block_it_goes_to_the_bracket_closing_it()
    {
        using var tab = Tab("a.cs", Code);
        tab.MoveCursor(4, 4);

        Assert.True(tab.GoToMatchingBracket());

        Assert.Equal((5, 4), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Outside_every_block_there_is_nowhere_to_go()
    {
        using var tab = Tab("a.cs", Code);
        tab.MoveCursor(0, 2);

        Assert.False(tab.GoToMatchingBracket());

        Assert.Equal((0, 2), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void A_partner_far_below_the_lexed_rows_is_still_found_by_the_jump()
    {
        var body = Enumerable.Range(0, 2_000).Select(i => $"    Step({i}); // ) }}");
        using var tab = Tab("a.cs", string.Join('\n', ["{", .. body, "}"]));
        tab.MoveCursor(0, 0);

        Assert.True(tab.GoToMatchingBracket());

        Assert.Equal((2_001, 0), (tab.CursorRow, tab.CursorColumn));
    }

    private static SyntaxHighlighter Midnight()
    {
        var highlighter = new SyntaxHighlighter(GrammarBundle.Load());
        highlighter.UseTheme("midnight.json");
        return highlighter;
    }

    private EditorTab Tab(string name, string content)
    {
        _fs.AddFile($"/work/{name}", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New($"/work/{name}"), Syntax) { App = _app, Width = 60, Height = 20 };
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
