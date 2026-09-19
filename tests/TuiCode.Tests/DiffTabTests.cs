using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

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

        group.Relocate("/work/a.txt", "/work/b.txt");

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
    public void Scrolling_moves_both_sides_and_stops_at_the_last_row()
    {
        var saved = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var diff = Diff(saved, saved.Replace("line 20", "LINE 20"));

        diff.NewKeyDownEvent(Key.CursorDown);
        Assert.Equal(1, diff.TopRow);
        Assert.Equal("  2  line 2    │  2  line 2    ", Render(diff)[1]);

        diff.NewKeyDownEvent(Key.End);
        Assert.Equal(14, diff.TopRow);
        Assert.Equal(" 20- line 20   │ 20+ LINE 20   ", Render(diff)[^1]);

        diff.NewKeyDownEvent(Key.PageUp);
        Assert.Equal(8, diff.TopRow);
        diff.NewKeyDownEvent(Key.Home);
        Assert.Equal(0, diff.TopRow);
    }

    private DiffTab Diff(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New("/work/a.txt")) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File))
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

    private Color BackgroundAt(int row, int col) => _app.Driver!.Contents![row, col].Attribute!.Value.Background;

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
        Assert.Equal(1, diff.TopRow);
        Assert.Equal("a.txt ↔ saved", workbench.StatusBar.DisplayedText);
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

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
