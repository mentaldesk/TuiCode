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

// Ctrl+S over a file that changed underneath you (#267). Boots a TG Application — serialised (#77).
public class SaveConflictHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";

    private readonly MockFileSystem _fs = new();

    [Fact]
    public async Task Saving_over_a_file_that_changed_asks_first_and_writes_nothing()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var cancelFocused = false;

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => { asked = Message(Confirm(workbench)!); cancelFocused = !Confirm(workbench)!.ConfirmHasFocus; });

        Assert.Equal(
            """
            'a.txt' changed on disk since you opened it.
            This tab has unsaved changes.
            Saving would overwrite the newer file.
            """.ReplaceLineEndings("\n"),
            asked.ReplaceLineEndings("\n"));
        Assert.True(cancelFocused);
        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_leaves_the_file_on_disk_alone(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null);

        Assert.Equal("from the other branch\n", _fs.File.ReadAllText(Path));
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Fact]
    public async Task Overwrite_writes_the_buffer_and_arms_the_next_warning()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var written = "";
        var dirtyAfterSave = true;

        await HostSteps.Run(host,
            () => OpenWithAnExternalChange(workbench),
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(Key.Tab),
            () => Confirm(workbench)!.ConfirmHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null,
            () =>
            {
                written = _fs.File.ReadAllText(Path);
                dirtyAfterSave = workbench.Editor.Group.ActiveTab!.IsDirty;
                _fs.File.WriteAllText(Path, "changed again\n");
                workbench.Editor.Group.ActiveTab!.Content = "mine again\n";
            },
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => Confirm(workbench) is not null);

        Assert.Equal("mine\n", written);
        Assert.False(dirtyAfterSave);
        Assert.Equal("changed again\n", _fs.File.ReadAllText(Path));
    }

    [Fact]
    public async Task A_clean_tab_saves_without_asking()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                _fs.AddFile(Path, new MockFileData("one\n"));
                workbench.OpenFile(_fs.FileInfo.New(Path));
                _fs.File.WriteAllText(Path, "from the other branch\n");
            },
            () => workbench.Editor.Group.ActiveTab is not null,
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => _fs.File.ReadAllText(Path) == "one\n");

        Assert.Null(Confirm(workbench));
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

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
    }
}
