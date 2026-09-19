using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Syntax;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Themes;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TuiCode.Tests;

public class DiffTabGroupTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void CompareToSaved_opens_a_diff_tab_and_leaves_no_active_editor_tab()
    {
        using var group = new EditorGroup();
        var tab = OpenEdited(group, "/work/a.txt");

        var diff = group.CompareToSaved(tab);

        Assert.NotNull(diff);
        Assert.Equal("a.txt ↔ saved", diff.Title);
        Assert.Same(diff, group.ActiveDiffTab);
        Assert.Null(group.ActiveTab);
        Assert.Equal([tab, diff], group.TabCollection);
    }

    [Fact]
    public void CompareToSaved_again_for_the_same_file_focuses_the_open_diff_tab()
    {
        using var group = new EditorGroup();
        var tab = OpenEdited(group, "/work/a.txt");
        var first = group.CompareToSaved(tab);
        group.OpenOrFocus(tab.File);

        var second = group.CompareToSaved(tab);

        Assert.Same(first, second);
        Assert.Single(group.DiffTabs);
        Assert.Same(first, group.ActiveDiffTab);
    }

    [Fact]
    public void CompareToSaved_returns_null_when_the_buffer_matches_the_file()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\r\nbravo\r\n"));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));

        Assert.Null(group.CompareToSaved(tab));
        Assert.Empty(group.DiffTabs);
        Assert.Same(tab, group.ActiveTab);
    }

    [Fact]
    public void The_diff_recomputes_when_its_tab_becomes_active_again()
    {
        using var group = new EditorGroup();
        var tab = OpenEdited(group, "/work/a.txt");
        var diff = group.CompareToSaved(tab)!;
        group.OpenOrFocus(tab.File);
        tab.Content = "alpha\nbravo\ncharlie\ndelta";

        group.NextTab();

        Assert.Same(diff, group.ActiveDiffTab);
        Assert.Equal(DiffRowKind.RightOnly, diff.Diff.Rows[^1].Kind);
    }

    [Fact]
    public void Closing_a_file_tab_closes_its_diff_tabs()
    {
        using var group = new EditorGroup();
        var a = OpenEdited(group, "/work/a.txt");
        var b = OpenEdited(group, "/work/b.txt");
        group.CompareToSaved(a);
        var bDiff = group.CompareToSaved(b);
        group.OpenOrFocus(a.File);

        group.CloseActive();

        Assert.Equal([b, bDiff], group.TabCollection);
        Assert.Same(b, group.ActiveTab);
    }

    [Fact]
    public void CloseActive_on_a_diff_tab_closes_only_it()
    {
        using var group = new EditorGroup();
        var a = OpenEdited(group, "/work/a.txt");
        group.CompareToSaved(a);

        group.CloseActive();

        Assert.Empty(group.DiffTabs);
        Assert.Same(a, group.ActiveTab);
    }

    [Fact]
    public void Tab_cycling_and_focus_by_index_include_diff_tabs()
    {
        using var group = new EditorGroup();
        var a = OpenEdited(group, "/work/a.txt");
        var b = OpenEdited(group, "/work/b.txt");
        var diff = group.CompareToSaved(a);

        group.NextTab();
        Assert.Same(a, group.ActiveTab);
        group.PreviousTab();
        Assert.Same(diff, group.ActiveDiffTab);
        group.PreviousTab();
        Assert.Same(b, group.ActiveTab);
        Assert.True(group.FocusByIndex(2));
        Assert.Same(diff, group.ActiveDiffTab);
    }

    [Fact]
    public void Renaming_the_file_retitles_its_diff_tab()
    {
        using var group = new EditorGroup();
        var tab = OpenEdited(group, "/work/a.txt");
        var diff = group.CompareToSaved(tab)!;
        _fs.File.Move("/work/a.txt", "/work/b.txt");

        group.Relocate(_fs.Path.GetFullPath("/work/a.txt"), _fs.Path.GetFullPath("/work/b.txt"));

        Assert.Equal("b.txt ↔ saved", diff.Title);
    }

    [Fact]
    public void ReadLines_splits_the_file_as_the_editor_does()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\r\ntwo\n\nthree\n"));
        using var group = new EditorGroup();
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));

        Assert.Equal(tab.Lines, DiffTab.ReadLines(tab.File));
    }

    private EditorTab OpenEdited(EditorGroup group, string path)
    {
        _fs.AddFile(path, new MockFileData("alpha\nbravo\n"));
        var tab = group.OpenOrFocus(_fs.FileInfo.New(path));
        tab.Content = "alpha\nBRAVO\n";
        return tab;
    }
}

// Renders through a TG driver — serialised (#77).
public class DiffTabDrawTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffTabDrawTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void Draws_both_sides_with_gaps_where_a_side_has_no_line()
    {
        var diff = Diff("one\ntwo\nthree\nfour", "one\nTWO\nfour\nfivefivefivefive");

        var screen = Render(diff);

        Assert.Equal(
        [
            " saved         │ working copy  ",
            "  1  one       │  1  one       ",
            "  2- two       │  2+ TWO       ",
            "  3- three     │               ",
            "  4  four      │  3  four      ",
            "               │  4+ fivefivefi",
            "               │               ",
        ], screen);
    }

    [Fact]
    public void Changed_lines_are_tinted_and_gaps_are_not()
    {
        var diff = Diff("one\ntwo\nthree", "one\nTWO");

        Render(diff);

        var normal = diff.GetAttributeForRole(VisualRole.Editable);
        Assert.Equal(normal.Background, BackgroundAt(1, 5));
        Assert.NotEqual(normal.Background, BackgroundAt(2, 5));
        Assert.NotEqual(normal.Background, BackgroundAt(2, 21));
        Assert.NotEqual(BackgroundAt(2, 5), BackgroundAt(2, 21));
        Assert.Equal(BackgroundAt(2, 5), BackgroundAt(3, 5));
        Assert.Equal(normal.Background, BackgroundAt(3, 21));
    }

    [Fact]
    public void Up_and_down_move_the_current_row_and_scroll_to_keep_it_in_view()
    {
        var saved = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var diff = Diff(saved, saved.Replace("line 20", "LINE 20"));

        diff.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal((1, 0), (diff.CurrentRow, diff.TopRow));

        diff.NewKeyDownEvent(Key.End);
        Assert.Equal((19, 14), (diff.CurrentRow, diff.TopRow));
        Assert.Equal(" 20- line 20   │ 20+ LINE 20   ", Render(diff)[^1]);

        diff.NewKeyDownEvent(Key.PageUp);
        Assert.Equal((13, 8), (diff.CurrentRow, diff.TopRow));
        diff.NewKeyDownEvent(Key.CursorUp);
        Assert.Equal((12, 8), (diff.CurrentRow, diff.TopRow));
        diff.NewKeyDownEvent(Key.Home);
        Assert.Equal((0, 0), (diff.CurrentRow, diff.TopRow));
    }

    [Fact]
    public void Right_scrolls_both_sides_a_quarter_of_the_text_width_and_keeps_the_gutter_in_place()
    {
        var diff = Diff("one\nabcdefghijklmnopqrstuvwxyz", "one\nabcdefghijklmnopqrstuvwxyZ");

        Press(diff, Key.CursorRight, 2);
        Assert.Equal(4, diff.LeftColumn);

        Assert.Equal(
        [
            " saved         │ working copy  ",
            "  1            │  1            ",
            "  2- efghijklmn│  2+ efghijklmn",
        ], Render(diff)[..3]);
    }

    [Fact]
    public void Sideways_scrolling_stops_at_the_left_edge_and_at_the_end_of_the_widest_line()
    {
        var diff = Diff("one\nabcdefghijklmnopqrstuvwxyz", "one\nabcdefghijklmnopqrstuvwxyZ");

        Press(diff, Key.CursorRight, 30);
        Assert.Equal(16, diff.LeftColumn);
        Assert.Equal("  2- qrstuvwxyz│  2+ qrstuvwxyZ", Render(diff)[2]);

        Press(diff, Key.CursorLeft, 30);
        Assert.Equal(0, diff.LeftColumn);
        Assert.Equal("  2- abcdefghij│  2+ abcdefghij", Render(diff)[2]);
    }

    [Fact]
    public void Tabs_and_wide_glyphs_cut_by_the_left_edge_show_as_blanks()
    {
        var diff = Diff("\tabcdefghijklmn\nx日本語abcdefghijk", "\tabcdefghijklmN\nx日本語abcdefghijK");

        Press(diff, Key.CursorRight, 1);

        var screen = Render(diff);
        Assert.Equal("  1-   abcdefgh│  1+   abcdefgh", screen[1]);
        Assert.Equal("  2-  本 語 abcde│  2+  本 語 abcde", screen[2]);
    }

    [Fact]
    public void The_horizontal_wheel_scrolls_sideways()
    {
        var diff = Diff("abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnopqrstuvwxyZ");

        diff.NewMouseEvent(new Mouse { Flags = MouseFlags.WheeledRight, Position = new System.Drawing.Point(5, 1) });
        diff.NewMouseEvent(new Mouse { Flags = MouseFlags.WheeledRight, Position = new System.Drawing.Point(5, 1) });
        diff.NewMouseEvent(new Mouse { Flags = MouseFlags.WheeledLeft, Position = new System.Drawing.Point(5, 1) });

        Assert.Equal(2, diff.LeftColumn);
    }

    [Fact]
    public void Moving_between_rows_and_changes_keeps_the_sideways_scroll()
    {
        var saved = Enumerable.Range(1, 30).Select(i => $"a long line number {i}").ToArray();
        var buffer = saved.ToArray();
        buffer[9] = "A LONG LINE NUMBER 10";
        buffer[24] = "A LONG LINE NUMBER 25";
        var diff = Diff(string.Join('\n', saved), string.Join('\n', buffer));
        Press(diff, Key.CursorRight, 3);

        diff.NextChange();
        diff.PreviousChange();
        diff.NewKeyDownEvent(Key.End);
        diff.NewKeyDownEvent(Key.Home);
        diff.Refresh();

        Assert.Equal(6, diff.LeftColumn);
    }

    [Fact]
    public void The_current_row_has_its_line_numbers_marked_on_both_sides()
    {
        var diff = Diff("one\ntwo\nthree", "one\nthree");
        diff.NewKeyDownEvent(Key.CursorDown);

        Render(diff);

        var focus = diff.GetAttributeForRole(VisualRole.Focus);
        Assert.Equal(focus, AttributeAt(2, 0));
        Assert.Equal(focus, AttributeAt(2, 16));
        Assert.NotEqual(focus, AttributeAt(2, 5));
        Assert.NotEqual(focus, AttributeAt(1, 0));
    }

    [Fact]
    public void Next_change_shows_the_change_below_a_little_context_and_stops_at_the_last()
    {
        var saved = Enumerable.Range(1, 30).Select(i => $"line {i}").ToArray();
        var buffer = saved.ToArray();
        buffer[9] = "LINE 10";
        buffer[24] = "LINE 25";
        var diff = Diff(string.Join('\n', saved), string.Join('\n', buffer));
        Assert.Equal("2 changes", diff.ChangeStatus);

        diff.NextChange();
        Assert.Equal((9, 7), (diff.CurrentRow, diff.TopRow));
        Assert.Equal("Change 1 of 2", diff.ChangeStatus);

        diff.NextChange();
        diff.NextChange();
        Assert.Equal((24, 22), (diff.CurrentRow, diff.TopRow));
        Assert.Equal("Change 2 of 2", diff.ChangeStatus);

        diff.PreviousChange();
        diff.PreviousChange();
        Assert.Equal(9, diff.CurrentRow);
        Assert.Equal("Change 1 of 2", diff.ChangeStatus);
    }

    [Fact]
    public void Change_status_says_so_when_there_are_no_changes()
    {
        var diff = Diff("one", "one");

        diff.NextChange();

        Assert.Equal(0, diff.CurrentRow);
        Assert.Equal("No changes", diff.ChangeStatus);
    }

    private const string DarkKeyword = "#569CD6";
    private const string LightKeyword = "#0000FF";

    [Fact]
    public void Both_sides_colour_keywords_and_changed_rows_keep_their_tint()
    {
        var diff = Diff("int a;\nint b;", "int a;\nint c;", new SyntaxHighlighter(GrammarBundle.Load()), "/work/a.cs");

        Render(diff);

        var normal = diff.GetAttributeForRole(VisualRole.Editable);
        foreach (var col in new[] { 5, 21 })
        {
            Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(1, col).Foreground);
            Assert.Equal(normal.Background, BackgroundAt(1, col));
            Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(2, col).Foreground);
            Assert.Equal(BackgroundAt(2, col - 5), BackgroundAt(2, col));
            Assert.NotEqual(normal.Background, BackgroundAt(2, col));
        }
    }

    [Fact]
    public void Syntax_colours_and_tints_stay_with_the_text_when_scrolled_sideways()
    {
        var diff = Diff("int ab; int bbbbbbbbbbbbbbbbbb;", "int ab; int cccccccccccccccccc;", new SyntaxHighlighter(GrammarBundle.Load()), "/work/a.cs");

        Press(diff, Key.CursorRight, 4);
        Render(diff); // A cold grammar can time out mid-line; the next draw re-lexes it.

        Assert.Equal("  1- int bbbbbb│  1+ int cccccc", Render(diff)[1]);
        var normal = diff.GetAttributeForRole(VisualRole.Editable);
        foreach (var col in new[] { 5, 21 })
        {
            Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(1, col).Foreground);
            Assert.NotEqual(Color.Parse(DarkKeyword), AttributeAt(1, col + 4).Foreground);
            Assert.NotEqual(normal.Background, BackgroundAt(1, col));
            Assert.Equal(BackgroundAt(1, col), BackgroundAt(1, col + 4));
        }
    }

    [Fact]
    public void Both_sides_follow_a_grammar_pinned_on_the_source_tab()
    {
        var syntax = new SyntaxHighlighter(GrammarBundle.Load());
        var diff = Diff("int a;", "int b;", syntax, "/work/a.txt");
        diff.Source.SetGrammar(syntax.LanguageById("csharp"));
        diff.Refresh();

        Render(diff);

        Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(1, 5).Foreground);
        Assert.Equal(Color.Parse(DarkKeyword), AttributeAt(1, 21).Foreground);
    }

    [Fact]
    public void A_file_with_no_grammar_is_drawn_plain()
    {
        var diff = Diff("int a;", "int b;", new SyntaxHighlighter(GrammarBundle.Load()), "/work/a.unknown");

        Render(diff);

        var normal = diff.GetAttributeForRole(VisualRole.Editable);
        Assert.Equal(normal.Foreground, AttributeAt(1, 5).Foreground);
        Assert.Equal(normal.Foreground, AttributeAt(1, 21).Foreground);
    }

    [Fact]
    public void Switching_theme_recolours_both_sides()
    {
        var syntax = new SyntaxHighlighter(GrammarBundle.Load());
        var diff = Diff("int a;", "int b;", syntax, "/work/a.cs");
        Render(diff);

        syntax.UseTheme(GrammarBundle.LightTheme);
        Render(diff);

        Assert.Equal(Color.Parse(LightKeyword), AttributeAt(1, 5).Foreground);
        Assert.Equal(Color.Parse(LightKeyword), AttributeAt(1, 21).Foreground);
    }

    [Fact]
    public void Only_the_lines_in_view_are_lexed()
    {
        var saved = string.Join('\n', Enumerable.Range(1, 1_000).Select(i => $"int a{i};"));
        var syntax = new SyntaxHighlighter(GrammarBundle.Load());
        var diff = Diff(saved, saved.Replace("a1;", "b1;"), syntax, "/work/a.cs");

        Render(diff);

        Assert.NotNull(diff.RightTokens!.TokensFor(5));
        Assert.Null(diff.RightTokens.TokensFor(6));
        Assert.Null(diff.LeftTokens!.TokensFor(6));
    }

    private DiffTab Diff(string saved, string buffer, SyntaxHighlighter? syntax = null, string path = "/work/a.txt")
    {
        _fs.AddFile(path, new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New(path), syntax) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File), syntax)
        {
            App = _app,
            Width = 31,
            Height = 7,
        };
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        return diff;
    }

    private static void Press(DiffTab diff, Key key, int times)
    {
        for (var i = 0; i < times; i++)
            diff.NewKeyDownEvent(key);
    }

    private Color BackgroundAt(int row, int col) => AttributeAt(row, col).Background;

    private Attribute AttributeAt(int row, int col) => _app.Driver!.Contents![row, col].Attribute!.Value;

    private string[] Render(DiffTab view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
        return Enumerable.Range(0, view.Frame.Height)
            .Select(row => string.Concat(Enumerable.Range(0, view.Frame.Width).Select(col => driver.Contents![row, col].Grapheme)))
            .ToArray();
    }
}

// Drives `cts` through the host. Boots a TG Application — serialised (#77).
public class CompareToSavedHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Cts_opens_one_diff_tab_that_arrow_keys_scroll()
    {
        var saved = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"line {i}"));
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                group.ActiveTab!.Content = saved.Replace("line 2\n", "LINE 2\n");
            },
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => host.App.InjectKey(Key.T),
            () => host.App.InjectKey(Key.S),
            () => group.ActiveDiffTab is not null,
            () => host.App.InjectKey(Key.CursorDown),
            () => commands.TryExecute(CommandIds.CompareToSaved));

        var diff = Assert.Single(group.DiffTabs);
        Assert.Equal("a.txt ↔ saved", diff.Title);
        Assert.Same(diff, group.ActiveDiffTab);
        Assert.Equal(1, diff.CurrentRow);
        Assert.StartsWith("a.txt ↔ saved  •  Change 1 of 1", workbench.StatusBar.DisplayedText);
    }

    private const string ThreeChangesStatus = "a.txt ↔ saved  •  Change {0} of 3  •  Alt+CursorDown next  Alt+CursorUp prev  Enter go to line";

    [Fact]
    public async Task Alt_down_and_alt_up_step_through_changes_and_stop_at_the_last()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var statuses = new List<string>();
        void Record() => statuses.Add(workbench.StatusBar.DisplayedText);

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            Record,
            () => host.App.InjectKey(Key.CursorUp.WithAlt),
            Record,
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            Record);

        Assert.Equal([string.Format(ThreeChangesStatus, 2), string.Format(ThreeChangesStatus, 1), string.Format(ThreeChangesStatus, 3)], statuses);
        Assert.Equal(30, workbench.Editor.Group.DiffTabs[0].CurrentRow);
    }

    [Fact]
    public async Task Enter_on_a_removed_row_goes_to_the_buffer_line_below_it_as_a_jump()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;
        EditorTab? landed = null;
        var row = -1;

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.Enter),
            () =>
            {
                landed = group.ActiveTab;
                row = landed!.CursorRow;
                commands.TryExecute(CommandIds.NavigateBack);
            });

        Assert.NotNull(landed);
        Assert.True(landed.ContentHasFocus);
        Assert.Equal(14, row);
        Assert.Equal("line 16", landed.Lines[row]);
        Assert.Equal(0, landed.CursorRow);
    }

    [Fact]
    public async Task The_status_hint_follows_a_rebind_and_clears_when_the_diff_tab_loses_focus()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        string? rebound = null;

        await HostSteps.Run(host,
            () =>
            {
                host.ApplyKeybindings(
                [
                    new KeybindingOverride(TestKeys.Chord("Alt+CursorDown"), "-" + CommandIds.NextChange),
                    new KeybindingOverride(TestKeys.Chord("F8"), CommandIds.NextChange),
                ]);
                OpenThreeChanges(workbench, commands);
            },
            () => host.App.InjectKey(Key.F8),
            () => { rebound = workbench.StatusBar.DisplayedText; },
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedText == "a.txt ↔ saved");

        Assert.Equal("a.txt ↔ saved  •  Change 1 of 3  •  F8 next  Alt+CursorUp prev  Enter go to line", rebound);
    }

    // Changes at rows 4 (modified), 14 (line 15 removed) and 30 (modified).
    private void OpenThreeChanges(Workbench.Workbench workbench, CommandService commands)
    {
        var saved = Enumerable.Range(1, 40).Select(i => $"line {i}").ToList();
        _fs.AddFile("/work/a.txt", new MockFileData(string.Join('\n', saved)));
        workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
        var buffer = saved.ToList();
        buffer[4] = "LINE 5";
        buffer[30] = "LINE 31";
        buffer.RemoveAt(14);
        workbench.Editor.Group.ActiveTab!.Content = string.Join('\n', buffer);
        commands.TryExecute(CommandIds.CompareToSaved);
    }

    [Fact]
    public async Task Closing_the_file_tab_closes_its_diff_tab()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                group.ActiveTab!.Content = "bravo\n";
                commands.TryExecute(CommandIds.CompareToSaved);
            },
            () => commands.TryExecute(CommandIds.PreviousEditor),
            () => commands.TryExecute(CommandIds.CloseActiveEditor));

        Assert.Empty(group.DiffTabs);
        Assert.Empty(group.TabCollection);
    }

    [Fact]
    public async Task Switching_theme_redraws_the_diff_tab()
    {
        _fs.AddFile("/work/a.cs", new MockFileData("int a;\n"));
        var settings = new InMemorySettingsService();
        using var workbench = BuildWorkbench(new SyntaxHighlighter(GrammarBundle.Load()));
        using var host = BuildHost(workbench, out var commands, settings);
        var group = workbench.Editor.Group;
        var redrawn = false;

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.cs"));
                group.ActiveTab!.Content = "int b;\n";
                commands.TryExecute(CommandIds.CompareToSaved);
            },
            () => group.ActiveDiffTab is { NeedsDraw: false },
            () =>
            {
                settings.Theme = BundledThemes.Daylight;
                redrawn = group.ActiveDiffTab!.NeedsDraw;
            });

        Assert.True(redrawn);
    }

    [Theory]
    [InlineData("unchanged", "No changes against saved")]
    [InlineData("deleted", "a.txt has never been saved.")]
    [InlineData("none", "No file is open.")]
    public async Task Cts_with_nothing_to_compare_says_why_and_opens_no_tab(string state, string message)
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                if (state != "none") workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                if (state == "deleted") _fs.File.Delete("/work/a.txt");
                commands.TryExecute(CommandIds.CompareToSaved);
            });

        Assert.Empty(workbench.Editor.Group.DiffTabs);
        Assert.Equal(message, workbench.StatusBar.DisplayedText);
    }

    private Workbench.Workbench BuildWorkbench(SyntaxHighlighter? syntax = null)
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(syntax), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, InMemorySettingsService? settings = null)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            settings ?? new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
