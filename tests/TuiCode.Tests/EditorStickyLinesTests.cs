using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Syntax;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Sticky lines in an editor tab (#474). Renders through a TG driver — serialised (#77).
public class EditorStickyLinesTests : StaticConfigurationTest
{
    private const int Width = 50;
    private const int Height = 14;

    private static readonly SyntaxHighlighter Syntax = new(GrammarBundle.Load());

    private static readonly string Code = string.Join('\n',
    [
        "namespace Demo;",
        "",
        "public sealed class Widget",
        "{",
        "    public void Run(int times)",
        "    {",
        .. Enumerable.Range(0, 30).Select(i => $"        Step({i});"),
        "    }",
        "",
        "    public void Stop()",
        "    {",
        .. Enumerable.Range(0, 30).Select(i => $"        Halt({i});"),
        "    }",
        "}",
    ]);

    private const int Class = 2;
    private const int Run = 4;
    private const int RunEnd = 36;
    private const int Stop = 38;

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorStickyLinesTests() => _app.Driver!.SetScreenSize(80, 30);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Inside_a_method_its_class_and_its_own_first_line_are_pinned_with_their_line_numbers()
    {
        using var tab = Tab("Widget.cs", Code);

        var screen = ScrollTo(tab, 20);

        Assert.Equal([Class, Run], tab.PinnedLines);
        Assert.Equal("  3  public sealed class Widget", screen[0].TrimEnd());
        Assert.Equal("  5      public void Run(int times)", screen[1].TrimEnd());
        Assert.Equal(" 23          Step(16);", screen[2].TrimEnd());
    }

    [Fact]
    public void Pinned_rows_keep_their_syntax_colours_on_the_current_line_background()
    {
        using var tab = Tab("Widget.cs", Code);

        ScrollTo(tab, 20);

        var editable = tab.TextView.GetAttributeForRole(VisualRole.Editable);
        var pinned = tab.TextView.PinnedAttribute(editable);
        Assert.NotEqual(editable.Background, pinned.Background);
        Assert.Equal(pinned.Background, AttributeAt(tab.TextView, 0, 0).Background);
        Assert.Equal(pinned.Background, AttributeAt(tab.TextView, 1, 40).Background);
        Assert.Equal("#569CD6", AttributeAt(tab.TextView, 0, 0).Foreground.ToString());
        Assert.Equal(pinned.Background, AttributeAt(tab, 0, 1).Background);
        Assert.Equal(editable.Background, AttributeAt(tab.TextView, 2, 0).Background);
    }

    [Fact]
    public void Scrolling_past_a_methods_closing_line_drops_it_and_the_next_method_takes_its_place()
    {
        using var tab = Tab("Widget.cs", Code);

        ScrollTo(tab, RunEnd);
        Assert.Equal([Class], tab.PinnedLines);

        ScrollTo(tab, Stop);
        Assert.Equal([Class, Stop], tab.PinnedLines);
    }

    [Fact]
    public void Moving_the_cursor_up_into_the_pinned_rows_scrolls_the_text_instead()
    {
        using var tab = Tab("Widget.cs", Code);
        ScrollTo(tab, 20);
        tab.MoveCursor(22, 0);
        Render(tab);

        tab.TextView.NewKeyDownEvent(Key.CursorUp);
        Render(tab);

        Assert.Equal(21, tab.CursorRow);
        Assert.Equal(19, tab.TopRow);
        Assert.Equal(tab.PinnedLines.Count, tab.CursorRow - tab.TopRow);
    }

    [Fact]
    public void A_jump_lands_its_line_two_rows_below_the_pinned_ones()
    {
        using var tab = Tab("Widget.cs", Code);
        Render(tab);

        tab.MoveCursor(50, 0);
        tab.RevealLines(50, 50);
        Render(tab);

        Assert.Equal([Class, Stop], tab.PinnedLines);
        Assert.Equal(Reveal.Margin, 50 - tab.TopRow - tab.PinnedLines.Count);
    }

    [Fact]
    public void Clicking_a_pinned_row_reports_its_line()
    {
        using var tab = Tab("Widget.cs", Code);
        ScrollTo(tab, 20);
        List<int> clicked = [];
        tab.PinnedLineClicked += (_, line) => clicked.Add(line);

        foreach (var flags in new[] { MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, MouseFlags.LeftButtonClicked })
            tab.TextView.NewMouseEvent(new Mouse { Flags = flags, Position = new Point(3, 1) });

        Assert.Equal([Run], clicked);
        Assert.Equal(0, tab.CursorRow);
    }

    [Fact]
    public void After_an_edit_the_pins_stay_put_until_the_rescan_is_done()
    {
        using var tab = Tab("Widget.cs", Code);
        ScrollTo(tab, 20);
        tab.StickyBudget = TimeSpan.Zero;

        tab.MoveCursor(0, 0);
        tab.TextView.InsertText("// header\n");
        ScrollTo(tab, 21);

        Assert.Equal([Class, Run], tab.PinnedLines);

        Finish(tab);
        Render(tab);
        var screen = Render(tab);
        Assert.Equal([Class + 1, Run + 1], tab.PinnedLines);
        Assert.Equal("  4  public sealed class Widget", screen[0].TrimEnd());
    }

    [Fact]
    public void An_editor_shorter_than_twelve_rows_pins_nothing()
    {
        using var tab = Tab("Widget.cs", Code, height: StickyLines.MinHeight - 1);

        ScrollTo(tab, 20);

        Assert.Empty(tab.PinnedLines);
    }

    [Fact]
    public void A_file_with_no_grammar_pins_nothing()
    {
        using var tab = Tab("Widget.txt", Code);

        ScrollTo(tab, 20);

        Assert.Empty(tab.PinnedLines);
    }

    [Fact]
    public void Turning_the_setting_off_removes_the_pins_at_once()
    {
        using var tab = Tab("Widget.cs", Code);
        ScrollTo(tab, 20);

        tab.Settings = tab.Settings with { StickyLines = false };
        var screen = Render(tab);

        Assert.Empty(tab.PinnedLines);
        Assert.Equal(" 21          Step(14);", screen[0].TrimEnd());
    }

    [Fact]
    public void With_word_wrap_a_long_pinned_line_shows_its_first_row_cut_with_an_ellipsis()
    {
        var code = Code.Replace("public void Run(int times)", "public void Run(int times, string label, bool loud)");
        using var tab = Tab("Widget.cs", code);
        tab.WordWrap = true;

        tab.MoveCursor(25, 0);
        tab.RevealLines(25, 25);
        Render(tab);
        var screen = Render(tab);

        Assert.Equal([Class, Run], tab.PinnedLines);
        Assert.Equal("  5      public void Run(int times, string …", screen[1].TrimEnd());
        Assert.DoesNotContain("label", screen[2]);
    }

    private EditorTab Tab(string name, string content, int height = Height)
    {
        _fs.AddFile($"/work/{name}", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New($"/work/{name}"), Syntax) { App = _app, Width = Width, Height = height };
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
        Render(tab);
        Finish(tab);
        return tab;
    }

    // Compiling a cold grammar's regexes can outrun the per-line limit; these tests are about what gets pinned.
    private static void Finish(EditorTab tab)
    {
        if (tab.StickyScan is not { } scan) return;
        scan.LineTimeLimit = TimeSpan.FromMinutes(1);
        scan.Advance(TimeSpan.MaxValue);
    }

    private string[] ScrollTo(EditorTab tab, int top)
    {
        var view = tab.TextView;
        view.Viewport = view.Viewport with { Y = top };
        Render(tab);
        return Render(tab);
    }

    private Attribute AttributeAt(View view, int row, int col)
    {
        var screen = view.ViewportToScreen(new Point(col, row));
        return _app.Driver!.Contents![screen.Y, screen.X].Attribute!.Value;
    }

    // Leaves out the last column, where the scroll bar is.
    private string[] Render(View view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.Layout();
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width - 1).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}
