using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Quitting with unsaved edits asks first (#411). Boots a TG Application — serialised (#77).
public class QuitDirtyHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();

    [Fact]
    public void One_file_is_listed_in_the_singular()
    {
        Assert.Equal(
            """
            1 open file has unsaved changes:
            a.txt
            Save them before quitting?
            """.ReplaceLineEndings("\n"),
            WorkbenchHost.UnsavedOnQuit(["a.txt"]));
    }

    [Fact]
    public void Four_files_are_all_listed()
    {
        Assert.Equal(
            """
            4 open files have unsaved changes:
            a.txt, b.txt, c.txt, d.txt
            Save them before quitting?
            """.ReplaceLineEndings("\n"),
            WorkbenchHost.UnsavedOnQuit(["a.txt", "b.txt", "c.txt", "d.txt"]));
    }

    [Fact]
    public void More_than_four_files_list_four_and_count_the_rest()
    {
        Assert.Equal(
            """
            6 open files have unsaved changes:
            a.txt, b.txt, c.txt, d.txt and 2 more
            Save them before quitting?
            """.ReplaceLineEndings("\n"),
            WorkbenchHost.UnsavedOnQuit(["a.txt", "b.txt", "c.txt", "d.txt", "e.txt", "f.txt"]));
    }

    [Fact]
    public async Task Quitting_with_dirty_tabs_asks_first_with_cancel_focused()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var title = "";
        var asked = "";
        var focused = "";
        string[] buttons = [];

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine a\n");
                Open(workbench, "/work/b.txt", edit: null);
                Open(workbench, "/work/c.txt", edit: "mine c\n");
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            () =>
            {
                var view = Confirm(workbench)!;
                (title, asked, focused) = (view.Title, Message(view), view.FocusedChoice!);
                buttons = [.. view.SubViews.OfType<Button>().Select(b => b.Text)];
            });

        Assert.Equal("Unsaved changes", title);
        Assert.Equal(WorkbenchHost.UnsavedOnQuit(["a.txt", "c.txt"]), asked.ReplaceLineEndings("\n"));
        Assert.Equal(["Save all", "Don't save", "Cancel"], buttons);
        Assert.Equal("Cancel", focused);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_keeps_the_app_running_with_everything_dirty(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var stillRunning = false;

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt", edit: "mine\n"),
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null,
            () => { stillRunning = true; });

        Assert.True(stillRunning);
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Save_all_writes_every_dirty_tab_then_quits()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.RunUntilQuit(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine a\n");
                Open(workbench, "/work/b.txt", edit: "mine b\n");
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("mine b\n", _fs.File.ReadAllText("/work/b.txt"));
        Assert.DoesNotContain(workbench.Editor.Group.Tabs, t => t.IsDirty);
    }

    [Fact]
    public async Task Dont_save_quits_and_leaves_the_files_alone()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.RunUntilQuit(host,
            () => Open(workbench, "/work/a.txt", edit: "mine\n"),
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Don't save"),
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task A_failed_write_keeps_the_app_open_and_the_tab_dirty()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var stillRunning = false;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine a\n");
                Open(workbench, "/work/b.txt", edit: "mine b\n");
                _fs.File.SetAttributes("/work/b.txt", FileAttributes.ReadOnly);
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null,
            () => { stillRunning = true; });

        Assert.True(stillRunning);
        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal(["b.txt"], workbench.Editor.Group.Tabs.Where(t => t.IsDirty).Select(t => t.File.Name));
    }

    [Theory]
    [InlineData("Compare")]
    [InlineData("Reload")]
    [InlineData("Cancel")]
    public async Task A_conflict_not_overwritten_keeps_the_app_open(string choice)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var stillRunning = false;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine\n");
                _fs.File.WriteAllText("/work/a.txt", "theirs\n");
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title == "File changed on disk",
            Reach(host, workbench, choice),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title != "File changed on disk",
            () => { stillRunning = true; });

        Assert.True(stillRunning);
        Assert.Equal("theirs\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task A_conflict_overwritten_saves_and_quits()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.RunUntilQuit(host,
            () =>
            {
                Open(workbench, "/work/a.txt", edit: "mine\n");
                _fs.File.WriteAllText("/work/a.txt", "theirs\n");
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title == "File changed on disk",
            Reach(host, workbench, "Overwrite"),
            () => host.App.InjectKey(Key.Enter));

        Assert.Equal("mine\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task The_quit_command_asks_as_ctrl_q_does()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Open(workbench, "/work/a.txt", edit: "mine\n"),
            () => _commands.TryExecute(CommandIds.Quit),
            () => Confirm(workbench) is not null);

        Assert.Equal("q", CommandMnemonics.For(CommandIds.Quit));
    }

    [Fact]
    public async Task Nothing_dirty_quits_at_once()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = false;
        workbench.SubViewAdded += (_, e) => asked |= e.SubView is ConfirmView;

        await HostSteps.RunUntilQuit(host,
            () => Open(workbench, "/work/a.txt", edit: null),
            () => host.App.InjectKey(Key.Q.WithCtrl));

        Assert.False(asked);
    }

    // Cancel starts focused, so Tab round the row until the wanted button has it.
    private static Func<bool> Reach(WorkbenchHost host, Workbench.Workbench workbench, string label) => () =>
    {
        if (Confirm(workbench)?.FocusedChoice == label) return true;
        host.App.InjectKey(Key.Tab);
        return false;
    };

    private void Open(Workbench.Workbench workbench, string path, string? edit)
    {
        _fs.AddFile(path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(path));
        if (edit is not null) workbench.Editor.Group.ActiveTab!.Content = edit;
    }

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private static string Message(ConfirmView view) => view.SubViews.OfType<Label>().First().Text;

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
