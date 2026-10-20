using MentalDesk.Tui.Dialogs;
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

// Ctrl+S over a file that changed underneath you (#267), and the ways out of it (#270).
// Boots a TG Application — serialised (#77).
public class SaveConflictHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";

    private readonly MockFileSystem _fs = new();
    private readonly CountingScopes _scopes = new();

    [Fact]
    public async Task Saving_over_a_file_that_changed_asks_first_and_writes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var focused = "";

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); focused = Confirm(workbench)!.FocusedButton()!; });

        Assert.Equal(
            """
            'a.txt' changed on disk since you opened it.
            This tab has unsaved changes.
            What would you like to do?
            """.ReplaceLineEndings("\n"),
            asked.ReplaceLineEndings("\n"));
        Assert.Equal("Esc Cancel", focused);
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Fact]
    public async Task The_conflict_offers_compare_overwrite_and_reload_before_cancel()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        string[] buttons = [], kinds = [];
        var width = 0;
        var overflowing = Array.Empty<string>();

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () =>
            {
                var view = Confirm(workbench)!;
                buttons = view.Buttons();
                kinds = view.Kinds();
                width = view.Frame.Width;
                overflowing = [.. view.SubViews.OfType<Button>()
                    .Where(b => b.Frame.X < 0 || b.Frame.Right > view.Viewport.Width)
                    .Select(b => b.Text.Trim())];
            });

        Assert.Equal(["Ctrl+Enter Compare", "Overwrite", "Reload", "Esc Cancel"], buttons);

        Assert.Equal(["Primary", "Danger", "Danger", "Secondary"], kinds);
        Assert.True(width <= 80, $"the modal is {width} columns wide");
        Assert.Empty(overflowing);
    }

    [Theory]
    [InlineData(false)] // Space presses the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_leaves_the_file_on_disk_alone(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Space),
            () => Confirm(workbench) is null);

        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task Overwrite_writes_the_buffer_and_arms_the_next_warning()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var written = "";
        var dirtyAfterSave = true;
        var balanced = false;

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Overwrite"),
            () => Confirm(workbench) is null,
            () =>
            {
                written = _fs.File.ReadAllText(Path);
                dirtyAfterSave = workbench.Editor.Group.ActiveTab!.IsDirty;
                balanced = _scopes.Depth == 1;
                _fs.File.WriteAllText(Path, "changed again\n");
                workbench.Editor.Group.ActiveTab!.Content = "mine again\n";
            },
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null);

        Assert.Equal("mine\n", written);
        Assert.False(dirtyAfterSave);
        Assert.True(balanced);
        Assert.Equal("changed again\n", _fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task Compare_shows_the_buffer_against_the_file_on_disk()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var focused = false;

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Compare"),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => { focused = workbench.Editor.Group.ActiveDiffTab!.IsFocused; });

        Assert.Null(Confirm(workbench));
        Assert.Equal(1, _scopes.Depth);
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.Equal("saved", workbench.Editor.Group.ActiveDiffTab!.LeftLabel);
        Assert.True(workbench.Editor.Group.ActiveDiffTab!.Source!.IsDirty);
        Assert.True(focused);
    }

    [Fact]
    public async Task Reload_takes_their_version_and_leaves_nothing_left_to_warn_about()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var title = "";
        var marked = DiskState.Changed;
        var dirty = true;

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => { workbench.Editor.Group.ActiveTab!.MarkOnDisk(DiskState.Changed); },
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            ConfirmDialogs.Choose(host, workbench, "Reload"),
            () => Confirm(workbench) is null,
            () =>
            {
                var tab = workbench.Editor.Group.ActiveTab!;
                (title, marked, dirty) = (tab.Title, tab.DiskMarker, tab.IsDirty);
            },
            // Ctrl+S straight afterwards: the recorded state is the file that's now there, so nothing asks again.
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => _fs.File.ReadAllText(Path) == "from the other branch\n");

        Assert.Equal("from the other branch\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
        Assert.Equal("a.txt", title);
        Assert.Equal(DiskState.Unchanged, marked);
        Assert.False(dirty);
        Assert.Null(Confirm(workbench));
        Assert.Equal(1, _scopes.Depth);
    }

    [Fact]
    public async Task A_clean_tab_is_asked_about_too_because_its_text_is_the_older_one()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
                _fs.File.WriteAllText(Path, "from the other branch\n");
            },
            () => workbench.Editor.Group.ActiveTab is not null,
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); });

        Assert.Equal(
            """
            'a.txt' changed on disk since you opened it.
            Saving puts this tab's older text back.
            What would you like to do?
            """.ReplaceLineEndings("\n"),
            asked.ReplaceLineEndings("\n"));
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task A_dirty_tab_nobody_else_touched_saves_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
            },
            () => workbench.Editor.Group.ActiveTab is not null,
            () => { workbench.Editor.Group.ActiveTab!.Content = "mine\n"; },
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => _fs.File.ReadAllText(Path) == "mine\n");

        Assert.Null(Confirm(workbench));
    }


    private void OpenWithAnExternalChange(Workbench.Workbench workbench)
    {
        _fs.AddFile(Path, new MockFileData("one\n"));
        workbench.OpenFile(_fs.FileInfo.New(Path));
        workbench.Editor.Group.ActiveTab!.Content = "mine\n";
        _fs.File.WriteAllText(Path, "from the other branch\n");
    }

    private static ConfirmDialog? Confirm(Workbench.Workbench workbench) => ConfirmDialogs.Of(workbench);

    private static string Message(ConfirmDialog view) => view.Message();

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), _scopes,
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }

    // Only the workbench scope should be left once a modal has closed, whichever way out was taken.
    private sealed class CountingScopes : IInputScopeStack
    {
        private readonly InputScopeStack _inner = new();

        public int Depth { get; private set; }

        public void Push(IKeybindingService scope) { _inner.Push(scope); Depth++; }

        public void Pop(IKeybindingService scope) { _inner.Pop(scope); Depth--; }

        public KeyHandlingResult Handle(Key key) => _inner.Handle(key);
    }
}
