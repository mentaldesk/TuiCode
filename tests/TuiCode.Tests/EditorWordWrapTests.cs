using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Point = System.Drawing.Point;

namespace TuiCode.Tests;

// Soft wrap in an editor tab (#378). Renders through a TG driver — serialised (#77).
public class EditorWordWrapTests : StaticConfigurationTest
{
    // A 20-column tab: a 5-column gutter, 15 for text, of which 13 wrap (a column each for the scroll bar and the caret).
    private const int Width = 20;
    private const int Height = 6;
    private const string Paragraph = "one two three four five six seven";

    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public EditorWordWrapTests() => _app.Driver!.SetScreenSize(40, 12);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_long_line_continues_on_the_rows_below_with_only_its_first_row_numbered()
    {
        using var tab = Tab($"{Paragraph}\nend");

        tab.WordWrap = true;

        Assert.Equal(
        [
            "  1  one two three",
            "     four five six",
            "     seven",
            "  2  end",
        ], Render(tab)[..4].Select(row => row.TrimEnd()));
    }

    [Fact]
    public void Toggling_it_off_puts_the_line_back_on_one_row()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;
        Render(tab);

        tab.WordWrap = false;

        Assert.Equal(["  1  one two three f", "  2  end"], Render(tab)[..2].Select(row => row.TrimEnd()));
    }

    [Fact]
    public void A_change_marker_covers_every_row_of_its_line()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;

        tab.Replace(new TextMatch(0, 0, 3), "ONE");
        var screen = Render(tab);

        Assert.Equal(["▎", "▎", "▎", " "], screen[..4].Select(row => row[4..5]));
    }

    [Fact]
    public void Find_highlights_and_the_selection_are_drawn_on_the_right_cells_of_a_continuation_row()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;

        tab.SetHighlights([new TextMatch(0, 19, 4)]);
        tab.Select(new TextMatch(0, 28, 5));
        Render(tab);

        var view = tab.TextView;
        Assert.Equal(view.GetAttributeForRole(VisualRole.Highlight), AttributeAt(view, 1, 5));
        Assert.Equal(view.GetAttributeForRole(VisualRole.Editable), AttributeAt(view, 1, 4));
        Assert.Equal(view.GetAttributeForRole(VisualRole.Active), AttributeAt(view, 2, 0));
        Assert.Equal(view.GetAttributeForRole(VisualRole.Editable), AttributeAt(view, 2, 5));
    }

    [Fact]
    public void Syntax_colours_follow_the_characters_onto_a_continuation_row()
    {
        var highlighter = new SyntaxHighlighter(GrammarBundle.Load());
        _fs.AddFile("/work/a.cs", new MockFileData("var text = 1; // a comment that wraps"));
        using var tab = new EditorTab(_fs.FileInfo.New("/work/a.cs"), highlighter) { App = _app, Width = Width, Height = Height };
        Init(tab);
        tab.WordWrap = true;

        var screen = Render(tab);

        Assert.StartsWith("     // a comment", screen[1]);
        Assert.Equal("#6A9955", AttributeAt(tab.TextView, 1, 0).Foreground.ToString());
        Assert.Equal("#6A9955", AttributeAt(tab.TextView, 1, 11).Foreground.ToString());
    }

    [Fact]
    public void The_caret_on_a_continuation_row_is_shown_there()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;

        tab.MoveCursor(0, 16);
        Render(tab);

        Assert.Equal(tab.TextView.ViewportToScreen(new Point(2, 1)), tab.TextView.Cursor.Position);
    }

    [Fact]
    public void Typing_on_a_continuation_row_edits_there_and_rewraps_the_line()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.MoveCursor(0, 14);
        Render(tab);

        tab.TextView.NewKeyDownEvent(Key.X);
        tab.TextView.NewKeyDownEvent(Key.Enter);
        var screen = Render(tab);

        Assert.Equal("one two three x", tab.Lines[0]);
        Assert.Equal(["  1 one two three", "    x", "  2 four five six"], screen[..3].Select(row => row.Remove(4, 1).TrimEnd()));
    }

    [Fact]
    public void Clicking_a_continuation_row_puts_the_caret_where_you_clicked()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        Render(tab);

        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new Point(5, 1) });
        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new Point(5, 1) });
        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonClicked, Position = new Point(5, 1) });

        Assert.Equal((0, 19), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Dragging_selects_across_rows()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        Render(tab);

        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed, Position = new Point(4, 0) });
        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport, Position = new Point(3, 2) });
        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new Point(3, 2) });

        Assert.Equal("two three four five six sev", tab.SelectedText);
    }

    [Fact]
    public void Moving_the_caret_below_the_view_scrolls_its_row_into_view()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 5)));
        tab.WordWrap = true;
        Render(tab);

        tab.MoveCursor(4, 30);
        var screen = Render(tab);

        Assert.Equal(14, tab.TextView.Viewport.Y + tab.TextView.Viewport.Height - 1);
        Assert.StartsWith("     seven ", screen[Height - 1]);
    }

    [Fact]
    public void Revealing_a_line_counts_screen_rows()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 10)));
        tab.WordWrap = true;
        Render(tab);

        tab.MoveCursor(8, 0);
        tab.RevealLines(8, 8);

        Assert.Equal(24 - Reveal.Margin, tab.TextView.Viewport.Y);
    }

    [Fact]
    public void The_horizontal_bar_hides_and_the_vertical_bar_counts_screen_rows()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 4)));
        Assert.True(tab.TextView.HorizontalScrollBar.Visible);

        tab.WordWrap = true;
        Render(tab);
        tab.Layout();

        Assert.False(tab.TextView.HorizontalScrollBar.Visible);
        Assert.True(tab.TextView.VerticalScrollBar.Visible);
        Assert.Equal(12, tab.TextView.VerticalScrollBar.ScrollableContentSize);
    }

    [Fact]
    public void Dragging_the_vertical_bar_scrolls_by_screen_rows()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 4)));
        tab.WordWrap = true;
        Render(tab);
        tab.Layout();

        tab.TextView.VerticalScrollBar.Value = 4;
        var screen = Render(tab);

        Assert.Equal(4, tab.TextView.Viewport.Y);
        Assert.StartsWith("     four five six", screen[0]);
    }

    [Fact]
    public void The_wheel_scrolls_one_screen_row()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 4)));
        tab.WordWrap = true;
        Render(tab);

        tab.TextView.NewMouseEvent(new Mouse { Flags = MouseFlags.WheeledDown, Position = new Point(1, 1) });

        Assert.Equal(1, tab.TextView.Viewport.Y);
    }

    [Fact]
    public void Resizing_rewraps_to_the_new_width()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        Render(tab);

        tab.Width = 30;
        var screen = Render(tab);

        Assert.Equal(["  1  one two three four five", "     six seven"], screen[..2].Select(row => row.TrimEnd()));
    }

    [Fact]
    public void Ln_and_Col_stay_the_file_line_and_column()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.MoveCursor(0, 30);
        Render(tab);

        Assert.Equal((0, 30), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Down_moves_onto_the_next_row_of_a_wrapped_line_and_then_the_next_line()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;
        tab.MoveCursor(0, 2);
        Render(tab);

        List<(int, int)> stops = [];
        for (var i = 0; i < 3; i++)
        {
            tab.TextView.NewKeyDownEvent(Key.CursorDown);
            stops.Add((tab.CursorRow, tab.CursorColumn));
        }
        Render(tab);

        Assert.Equal([(0, 16), (0, 30), (1, 2)], stops);
        Assert.Equal(tab.TextView.ViewportToScreen(new Point(2, 3)), tab.TextView.Cursor.Position);
    }

    [Fact]
    public void Up_moves_back_through_the_rows_of_the_line_above()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;
        tab.MoveCursor(1, 2);
        Render(tab);

        List<(int, int)> stops = [];
        for (var i = 0; i < 3; i++)
        {
            tab.TextView.NewKeyDownEvent(Key.CursorUp);
            stops.Add((tab.CursorRow, tab.CursorColumn));
        }

        Assert.Equal([(0, 30), (0, 16), (0, 2)], stops);
    }

    [Fact]
    public void Up_on_the_first_row_and_down_on_the_last_stay_put()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.MoveCursor(0, 2);

        tab.TextView.NewKeyDownEvent(Key.CursorUp);
        var top = (tab.CursorRow, tab.CursorColumn);
        tab.MoveCursor(0, 30);
        tab.TextView.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((0, 2), top);
        Assert.Equal((0, 30), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void The_screen_column_sticks_through_a_shorter_row()
    {
        using var tab = Tab($"{Paragraph}\nab\n{Paragraph}");
        tab.WordWrap = true;
        tab.MoveCursor(0, 22);

        tab.TextView.NewKeyDownEvent(Key.CursorDown);
        tab.TextView.NewKeyDownEvent(Key.CursorDown);
        var shortRow = (tab.CursorRow, tab.CursorColumn);
        tab.TextView.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((1, 2), shortRow);
        Assert.Equal((2, 8), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Shift_down_and_up_extend_the_selection_one_row()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.MoveCursor(0, 4);

        tab.TextView.NewKeyDownEvent(Key.CursorDown.WithShift);
        var down = tab.SelectedText;
        tab.TextView.NewKeyDownEvent(Key.CursorDown.WithShift);
        tab.TextView.NewKeyDownEvent(Key.CursorUp.WithShift);

        Assert.Equal("two three four", down);
        Assert.Equal("two three four", tab.SelectedText);
    }

    [Fact]
    public void Page_down_and_up_move_a_page_of_screen_rows()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 5)));
        tab.WordWrap = true;
        tab.MoveCursor(0, 2);
        Render(tab);

        tab.TextView.NewKeyDownEvent(Key.PageDown);
        var down = (tab.CursorRow, tab.CursorColumn);
        Render(tab);
        var top = tab.TextView.Viewport.Y;
        tab.TextView.NewKeyDownEvent(Key.PageUp);

        Assert.Equal((2, 2), down);
        Assert.Equal(Height, top);
        Assert.Equal((0, 2), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Shift_page_down_selects_a_page_of_screen_rows()
    {
        using var tab = Tab(string.Join('\n', Enumerable.Repeat(Paragraph, 5)));
        tab.WordWrap = true;
        tab.MoveCursor(0, 0);
        Render(tab);

        tab.TextView.NewKeyDownEvent(Key.PageDown.WithShift);

        Assert.Equal($"{Paragraph}\n{Paragraph}\n".ReplaceLineEndings(), tab.SelectedText.ReplaceLineEndings());
    }

    [Fact]
    public void Unwrapped_down_still_moves_a_whole_line()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.MoveCursor(0, 2);

        tab.TextView.NewKeyDownEvent(Key.CursorDown);

        Assert.Equal((1, 2), (tab.CursorRow, tab.CursorColumn));
    }

    [Fact]
    public void Saving_writes_the_file_exactly_as_it_was()
    {
        var content = $"{Paragraph}\nend\n";
        using var tab = Tab(content);
        tab.WordWrap = true;
        Render(tab);

        tab.Save();

        Assert.Equal(content, _fs.File.ReadAllText("/work/file.txt"));
    }

    [Fact]
    public void Every_caret_is_drawn_on_its_own_row_including_continuation_rows()
    {
        using var tab = Tab($"{Paragraph}\n{Paragraph}");
        tab.WordWrap = true;
        tab.MoveCursor(0, 16);

        tab.AddCursor(LineDirection.Down);
        var screen = Render(tab);

        Assert.Equal([new Point(16, 0), new Point(16, 1)], tab.TextView.Carets.Select(c => c.Position));
        var editable = tab.TextView.GetAttributeForRole(VisualRole.Editable);
        foreach (var row in new[] { 1, 4 })
        {
            Assert.Equal("u", screen[row][7..8]);
            Assert.Equal(new Attribute(editable.Background, editable.Foreground), AttributeAt(tab.TextView, row, 2));
        }
    }

    [Fact]
    public void Alt_clicking_a_continuation_row_adds_a_caret_there_and_again_removes_it()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.MoveCursor(0, 2);
        Render(tab);
        Mouse AltClick() => new() { Flags = MouseFlags.LeftButtonClicked | MouseFlags.Alt, Position = new Point(5, 1) };

        tab.TextView.NewMouseEvent(AltClick());
        var added = tab.TextView.Carets.Select(c => c.Position).ToArray();
        tab.TextView.NewMouseEvent(AltClick());

        Assert.Equal([new Point(2, 0), new Point(19, 0)], added);
        Assert.Equal([new Point(2, 0)], tab.TextView.Carets.Select(c => c.Position));
    }

    [Fact]
    public void Typing_and_deleting_at_several_carets_edits_each_line_and_rewraps_it()
    {
        using var tab = Tab($"{Paragraph}\n{Paragraph}");
        tab.WordWrap = true;
        tab.MoveCursor(0, 14);
        tab.AddCursor(LineDirection.Down);

        tab.TextView.NewKeyDownEvent(Key.X);
        tab.TextView.NewKeyDownEvent(Key.X);
        var typedLines = tab.Lines.ToArray();
        var typed = Render(tab);
        tab.TextView.NewKeyDownEvent(Key.Backspace);

        Assert.Equal(["one two three xxfour five six seven", "one two three xxfour five six seven"], typedLines);
        Assert.Equal(["    six seven", "  2 one two three", "    six seven"],
            new[] { typed[2], typed[3], typed[5] }.Select(row => row.Remove(4, 1).TrimEnd()));
        Assert.Equal(["one two three xfour five six seven", "one two three xfour five six seven"], tab.Lines);
    }

    [Fact]
    public void Down_and_up_with_several_carets_move_each_one_screen_row()
    {
        using var tab = Tab($"{Paragraph}\n{Paragraph}");
        tab.WordWrap = true;
        tab.MoveCursor(0, 2);
        tab.AddCursor(LineDirection.Down);

        tab.TextView.NewKeyDownEvent(Key.CursorDown);
        var once = tab.TextView.Carets.Select(c => c.Position).ToArray();
        tab.TextView.NewKeyDownEvent(Key.CursorDown);
        var twice = tab.TextView.Carets.Select(c => c.Position).ToArray();
        tab.TextView.NewKeyDownEvent(Key.CursorUp);

        Assert.Equal([new Point(16, 0), new Point(16, 1)], once);
        Assert.Equal([new Point(30, 0), new Point(30, 1)], twice);
        Assert.Equal([new Point(16, 0), new Point(16, 1)], tab.TextView.Carets.Select(c => c.Position));
    }

    [Fact]
    public void Column_select_sweeps_the_cells_on_screen_not_the_columns_in_the_file()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;
        tab.ColumnSelect = true;
        tab.MoveCursor(0, 4);

        tab.TextView.NewKeyDownEvent(Key.CursorDown.WithShift);
        tab.TextView.NewKeyDownEvent(Key.CursorRight.WithShift);
        Render(tab);

        var view = tab.TextView;
        var selected = view.GetAttributeForRole(VisualRole.Active);
        Assert.Equal([(new Point(18, 0), new Point(19, 0)), (new Point(4, 0), new Point(5, 0))],
            view.Carets.Select(c => (c.Start, c.End)));
        Assert.Equal(selected, AttributeAt(view, 0, 4));
        Assert.Equal(selected, AttributeAt(view, 1, 4));
        Assert.Equal(view.GetAttributeForRole(VisualRole.Editable), AttributeAt(view, 1, 6));
    }

    [Fact]
    public void Column_select_reaches_from_a_wrapped_line_into_the_next_line()
    {
        using var tab = Tab($"{Paragraph}\nend");
        tab.WordWrap = true;
        tab.ColumnSelect = true;
        tab.MoveCursor(0, 30);

        tab.TextView.NewKeyDownEvent(Key.CursorDown.WithShift);
        for (var i = 0; i < 2; i++) tab.TextView.NewKeyDownEvent(Key.CursorLeft.WithShift);

        Assert.Equal([(new Point(0, 1), new Point(2, 1)), (new Point(28, 0), new Point(30, 0))],
            tab.TextView.Carets.Select(c => (c.Start, c.End)));
    }

    [Fact]
    public void Shift_End_in_column_select_takes_each_row_to_its_own_end()
    {
        using var tab = Tab(Paragraph);
        tab.WordWrap = true;
        tab.ColumnSelect = true;
        tab.MoveCursor(0, 0);

        tab.TextView.NewKeyDownEvent(Key.CursorDown.WithShift);
        tab.TextView.NewKeyDownEvent(Key.End.WithShift);

        Assert.Equal([(new Point(14, 0), new Point(27, 0)), (new Point(0, 0), new Point(13, 0))],
            tab.TextView.Carets.Select(c => (c.Start, c.End)));
    }

    [Fact]
    public void Every_tab_opens_unwrapped()
    {
        using var tab = Tab(Paragraph);

        Assert.False(tab.WordWrap);
    }

    private EditorTab Tab(string content)
    {
        _fs.AddFile("/work/file.txt", new MockFileData(content));
        var tab = new EditorTab(_fs.FileInfo.New("/work/file.txt")) { App = _app, Width = Width, Height = Height };
        Init(tab);
        return tab;
    }

    private static void Init(EditorTab tab)
    {
        tab.BeginInit();
        tab.EndInit();
        tab.Layout();
    }

    private Attribute AttributeAt(View view, int row, int col)
    {
        var screen = view.ViewportToScreen(new Point(col, row));
        return _app.Driver!.Contents![screen.Y, screen.X].Attribute!.Value;
    }

    private string[] Render(View view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.Layout();
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}

// Ctrl+T W through a TG Application — serialised (#77).
public class EditorWordWrapHostTests : StaticConfigurationTest
{
    [Fact]
    public async Task Ctrl_T_W_wraps_only_the_active_tab_and_the_status_bar_follows_the_active_tab()
    {
        var (fs, statusBar, workbench, host) = Start();
        using var _ = workbench;
        using var __ = host;
        EditorTab? a = null, b = null;
        string whileWrapped = "", onTheOtherTab = "", backAgain = "";

        await HostSteps.Run(host,
            () =>
            {
                b = workbench.Editor.Open(fs.FileInfo.New("/work/b.txt"));
                a = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                a.FocusContent();
            },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.W),
            () => { whileWrapped = statusBar.DisplayedText; },
            () => { workbench.Editor.Group.Focus(b!.File.FullName); },
            () => { onTheOtherTab = statusBar.DisplayedText; },
            () => { workbench.Editor.Group.Focus(a!.File.FullName); },
            () => { backAgain = statusBar.DisplayedText; });

        Assert.True(a!.WordWrap);
        Assert.False(b!.WordWrap);
        Assert.EndsWith("  •  Wrap", whileWrapped);
        Assert.DoesNotContain("Wrap", onTheOtherTab);
        Assert.EndsWith("  •  Wrap", backAgain);
    }

    [Fact]
    public async Task Ctrl_T_W_again_unwraps_the_tab()
    {
        var (fs, statusBar, workbench, host) = Start();
        using var _ = workbench;
        using var __ = host;
        EditorTab? tab = null;

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.W),
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.W));

        Assert.False(tab!.WordWrap);
        Assert.DoesNotContain("Wrap", statusBar.DisplayedText);
    }

    [Fact]
    public async Task Column_select_and_adding_a_cursor_work_while_the_tab_wraps_and_the_status_bar_shows_both_modes()
    {
        var (fs, statusBar, workbench, host) = Start();
        using var _ = workbench;
        using var __ = host;
        EditorTab? tab = null;
        string afterColumnSelect = "", afterAddCursor = "";

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
            },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.W),
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => { afterColumnSelect = statusBar.DisplayedText; },
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => { afterAddCursor = statusBar.DisplayedText; });

        Assert.True(workbench.Editor.Group.ColumnSelect);
        Assert.EndsWith("  •  Column select  •  Wrap", afterColumnSelect);
        Assert.True(tab!.HasSecondaryCursors);
        Assert.DoesNotContain("Not available", afterAddCursor);
    }

    [Fact]
    public async Task With_wrap_on_in_settings_a_file_opens_wrapped_and_Ctrl_T_W_still_unwraps_it()
    {
        var (fs, statusBar, workbench, host) = Start(EditorSettings.Default with { WordWrap = true });
        using var _ = workbench;
        using var __ = host;
        EditorTab? tab = null;
        string opened = "";

        await HostSteps.Run(host,
            () =>
            {
                tab = workbench.Editor.Open(fs.FileInfo.New("/work/a.txt"));
                tab.FocusContent();
            },
            () => { opened = statusBar.DisplayedText; },
            () => host.App.InjectKey(Key.T.WithCtrl),
            () => host.App.InjectKey(Key.W));

        Assert.EndsWith("  •  Wrap", opened);
        Assert.False(tab!.WordWrap);
        Assert.DoesNotContain("Wrap", statusBar.DisplayedText);
    }

    [Fact]
    public void Wrap_shows_after_the_grammar()
    {
        var statusBar = new StatusBarPart();

        statusBar.SetGrammar("Markdown");
        statusBar.SetWrap(true);

        Assert.EndsWith("Markdown  •  Wrap", statusBar.DisplayedText);
    }

    private static (MockFileSystem Fs, StatusBarPart StatusBar, Workbench.Workbench Workbench, WorkbenchHost Host) Start(
        EditorSettings? editor = null)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/work/a.txt", new MockFileData("one two three four five six seven eight nine ten eleven twelve\nend\n"));
        fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        var statusBar = new StatusBarPart();
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), statusBar);
        var commands = new CommandService();
        var host = new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService { Editor = editor ?? EditorSettings.Default }, driverName: DriverRegistry.Names.ANSI);
        return (fs, statusBar, workbench, host);
    }
}
