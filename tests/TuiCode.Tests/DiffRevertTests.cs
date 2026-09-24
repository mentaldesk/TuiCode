using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Revert change (`rc`) in a compare-to-saved diff (#245).
public class DiffRevertTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Revert_takes_back_lines_added_to_the_buffer()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree", "one\nstray\ntwo\nthree");
        diff.NextChange();

        Assert.Equal(1, diff.RevertChange());
        Assert.Equal(["one", "two", "three"], tab.Lines);
    }

    [Fact]
    public void Revert_puts_back_lines_removed_from_the_buffer()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree\nfour", "one\nfour");
        diff.NextChange();

        Assert.Equal(2, diff.RevertChange());
        Assert.Equal(["one", "two", "three", "four"], tab.Lines);
    }

    [Fact]
    public void Revert_replaces_modified_lines_with_the_saved_text()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree", "one\nTWO\nTHREE");
        diff.NextChange();

        Assert.Equal(2, diff.RevertChange());
        Assert.Equal(["one", "two", "three"], tab.Lines);
    }

    [Fact]
    public void Revert_leaves_the_tab_dirty_and_the_file_on_disk_untouched()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree", "ONE\nTWO\nthree");
        diff.NextChange();

        diff.RevertChange();

        Assert.True(tab.IsDirty);
        Assert.Equal("one\ntwo\nthree", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public void Revert_drops_the_change_count_by_one_and_leaves_the_cursor_where_it_was()
    {
        using var group = new EditorGroup();
        var (_, diff) = Compare(group, "one\ntwo\nthree\nfour\nfive", "one\nTWO\nthree\nfour\nFIVE");
        diff.NextChange();
        var row = diff.CurrentRow;

        diff.RevertChange();

        Assert.Equal("1 change", diff.ChangeStatus);
        Assert.Equal(row, diff.CurrentRow);
        Assert.All(diff.Diff.Rows.Take(2), r => Assert.Equal(DiffRowKind.Both, r.Kind));
    }

    [Fact]
    public void Revert_above_the_first_change_takes_the_first_one_and_shows_it()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo\nthree\nfour", "one\nTWO\nthree\nFOUR");

        Assert.Equal(1, diff.RevertChange());
        Assert.Equal(["one", "two", "three", "FOUR"], tab.Lines);
        Assert.Equal(1, diff.CurrentRow);
    }

    [Fact]
    public void Revert_with_nothing_left_to_revert_does_nothing()
    {
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, "one\ntwo", "one\nTWO");
        diff.RevertChange();

        Assert.Null(diff.RevertChange());
        Assert.Equal(["one", "two"], tab.Lines);
    }

    [Fact]
    public void Revert_does_nothing_in_a_diff_this_slice_does_not_cover()
    {
        using var group = new EditorGroup();
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo"));
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));
        tab.Content = "one\nTWO";
        var diff = group.Compare(tab, "HEAD", () => ["one", "two"])!;
        diff.NextChange();

        Assert.Null(diff.RevertChange());
        Assert.Equal(["one", "TWO"], tab.Lines);
    }

    [Fact]
    public void Revert_of_a_whole_file_collapsed_by_the_edit_limit_runs_in_one_go()
    {
        var saved = string.Join('\n', Enumerable.Range(0, 2 * DiffTab.MaxEdits).Select(i => $"line {i}"));
        using var group = new EditorGroup();
        var (tab, diff) = Compare(group, saved, saved.Replace("line ", "LINE "));
        diff.NextChange();

        Assert.Equal(2 * DiffTab.MaxEdits, diff.RevertChange());
        Assert.Equal(saved.Split('\n'), tab.Lines);
    }

    private (EditorTab Tab, DiffTab Diff) Compare(EditorGroup group, string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var tab = group.OpenOrFocus(_fs.FileInfo.New("/work/a.txt"));
        tab.Content = buffer;
        return (tab, group.CompareToSaved(tab)!);
    }
}

// Renders through a TG driver — serialised (#77).
public class DiffRevertDrawTests : StaticConfigurationTest
{
    private readonly IApplication _app = Application.Create().Init(DriverRegistry.Names.ANSI);
    private readonly MockFileSystem _fs = new();

    public DiffRevertDrawTests() => _app.Driver!.SetScreenSize(40, 10);

    public override void Dispose()
    {
        _app.Dispose();
        base.Dispose();
    }

    [Fact]
    public void The_reverted_rows_are_tinted_on_both_sides_until_the_next_change_move()
    {
        var diff = Compare("one\ntwo\nthree\nfour\nfive", "one\nTWO\nTHREE\nfour\nFIVE");
        diff.NextChange();
        diff.RevertChange();

        Render(diff);
        var normal = diff.GetAttributeForRole(VisualRole.Editable);
        var focus = diff.GetAttributeForRole(VisualRole.Focus);
        // y=1 is the first row; the revert covered the second and third.
        Assert.Equal(normal.Background, BackgroundAt(1, 5));
        Assert.Equal(focus.Background, BackgroundAt(3, 5));
        Assert.Equal(focus.Background, BackgroundAt(3, 21));
        Assert.Equal(normal.Background, BackgroundAt(4, 5));

        diff.NextChange();

        Render(diff);
        Assert.Equal(normal.Background, BackgroundAt(3, 5));
        Assert.Equal(normal.Background, BackgroundAt(3, 21));
    }

    private DiffTab Compare(string saved, string buffer)
    {
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        var source = new EditorTab(_fs.FileInfo.New("/work/a.txt")) { Content = buffer };
        var diff = new DiffTab(source, "saved", () => DiffTab.ReadLines(source.File))
        {
            App = _app,
            Width = 31,
            Height = 7,
            CanRevert = true,
        };
        diff.BeginInit();
        diff.EndInit();
        diff.Layout();
        diff.Refresh();
        return diff;
    }

    private Color BackgroundAt(int row, int col) => _app.Driver!.Contents![row, col].Attribute!.Value.Background;

    private void Render(DiffTab view)
    {
        var driver = _app.Driver!;
        driver.ClearContents();
        driver.Clip = new Region(driver.Screen);
        view.SetNeedsDraw();
        view.Draw();
    }
}

// Drives `rc` through the host. Boots a TG Application — serialised (#77).
public class RevertChangeHostTests : StaticConfigurationTest
{
    private const string Keys = "Alt+↓ next  Alt+↑ prev  Alt+→ revert  Enter go to line";

    private readonly MockFileSystem _fs = new();

    [Fact]
    public void Revert_change_is_a_diff_scoped_command_bound_to_alt_right()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        var descriptor = Assert.Single(commands.Registered, c => c.Id == CommandIds.RevertChange);
        Assert.Equal(("Revert change", CommandScope.Diff), (descriptor.Label, descriptor.Scope));
        Assert.Equal("rc", CommandMnemonics.For(CommandIds.RevertChange));
    }

    [Fact]
    public async Task Alt_right_reverts_the_change_and_says_how_many_lines_it_took_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorRight.WithAlt));

        Assert.Equal($"Reverted 1 line from saved  •  Change 1 of 2  •  {Keys}", workbench.StatusBar.DisplayedText);
        Assert.Equal(40, workbench.Editor.Group.Tabs[0].Lines.Count);
        Assert.Equal("line 15", workbench.Editor.Group.Tabs[0].Lines[14]);
    }

    [Fact]
    public async Task Ctrl_right_reverts_too_because_our_terminal_profiles_rewrite_alt_right_to_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => host.App.InjectKey(Key.CursorRight.WithCtrl));

        Assert.Equal($"Reverted 1 line from saved  •  2 changes  •  {Keys}", workbench.StatusBar.DisplayedText);
        Assert.Equal("line 5", workbench.Editor.Group.Tabs[0].Lines[4]);
    }

    [Fact]
    public async Task One_ctrl_z_in_the_editor_takes_the_whole_revert_back_with_the_cursor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () =>
            {
                OpenThreeChanges(workbench, commands);
                group.Tabs[0].MoveCursor(20, 0);
            },
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorRight.WithAlt),
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveTab is not null,
            () => host.App.InjectKey(Key.Z.WithCtrl));

        Assert.Equal("LINE 5", group.Tabs[0].Lines[4]);
        Assert.Equal(20, group.Tabs[0].CursorRow);
    }

    [Fact]
    public async Task A_revert_too_big_to_see_says_how_to_undo_it()
    {
        var saved = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"line {i}"));
        _fs.AddFile("/work/a.txt", new MockFileData(saved));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = saved.Replace("line ", "LINE ");
                commands.TryExecute(CommandIds.CompareToSaved);
            },
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.CursorRight.WithAlt));

        Assert.StartsWith("Reverted 60 lines from saved — Ctrl+Z to undo", workbench.StatusBar.DisplayedText);
        Assert.Equal(saved.Split('\n'), workbench.Editor.Group.Tabs[0].Lines);
    }

    [Fact]
    public async Task Rc_on_a_freshly_opened_diff_reverts_its_first_change()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenThreeChanges(workbench, commands),
            () => host.App.InjectKey(Key.CursorRight.WithAlt));

        Assert.Equal($"Reverted 1 line from saved  •  2 changes  •  {Keys}", workbench.StatusBar.DisplayedText);
        Assert.Equal("line 5", workbench.Editor.Group.Tabs[0].Lines[4]);
    }

    [Fact]
    public async Task Rc_in_a_diff_it_does_not_cover_says_what_it_needs()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                var tab = workbench.Editor.Group.ActiveTab!;
                tab.Content = "alpha\nBRAVO";
                workbench.Editor.Group.Compare(tab, "HEAD", () => ["alpha", "bravo"]);
            },
            () => host.App.InjectKey(Key.CursorRight.WithAlt));

        Assert.StartsWith("Revert needs a diff against saved, not HEAD", workbench.StatusBar.DisplayedText);
        Assert.Equal("BRAVO", workbench.Editor.Group.Tabs[0].Lines[1]);
    }

    [Fact]
    public async Task Rc_on_a_diff_with_nothing_left_to_revert_says_no_changes()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = "alpha\nBRAVO";
                commands.TryExecute(CommandIds.CompareToSaved);
            },
            () => host.App.InjectKey(Key.CursorRight.WithAlt),
            () => host.App.InjectKey(Key.CursorRight.WithAlt));

        Assert.StartsWith("No changes  •  No changes", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_hint_bar_follows_a_rebound_revert_key()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                host.ApplyKeybindings(
                [
                    new KeybindingOverride(TestKeys.Chord("Alt+CursorRight"), "-" + CommandIds.RevertChange),
                    new KeybindingOverride(TestKeys.Chord("Ctrl+CursorRight"), "-" + CommandIds.RevertChange),
                    new KeybindingOverride(TestKeys.Chord("F9"), CommandIds.RevertChange),
                ]);
                OpenThreeChanges(workbench, commands);
            },
            () => host.App.InjectKey(Key.CursorDown.WithAlt),
            () => host.App.InjectKey(Key.F9));

        Assert.Equal("Reverted 1 line from saved  •  2 changes  •  Alt+↓ next  Alt+↑ prev  F9 revert  Enter go to line",
            workbench.StatusBar.DisplayedText);
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
