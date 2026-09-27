using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// A file that goes away under an open tab (#271): what the tab says, and what Ctrl+S then does about it.
// Boots a TG Application — serialised (#77).
public class DiskDeleteHostTests : StaticConfigurationTest
{
    private const string Path = "/work/a.txt";
    private const string Note = "⊘ a.txt no longer exists on disk — Ctrl+S writes it back";

    private readonly WatchableFileSystem _fs = new();

    [Fact]
    public async Task Deleting_the_file_marks_the_tab_and_says_so_without_a_keypress()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";
        var title = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New(Path)),
            () => workbench.Editor.Group.ActiveTab is not null,
            () => Delete(Path),
            () => workbench.StatusBar.DisplayedText.StartsWith(Note, StringComparison.Ordinal),
            () => { said = workbench.StatusBar.DisplayedText; title = workbench.Editor.Group.ActiveTab!.Title; });

        Assert.StartsWith(Note, said);
        Assert.Equal("a.txt ⊘ ", title);
        Assert.Equal("one\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    // Nothing on disk to overwrite, so nothing to ask about: the buffer goes straight back.
    [Fact]
    public async Task Ctrl_S_on_a_deleted_file_writes_it_back_with_no_modal()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var title = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New(Path)),
            () => workbench.Editor.Group.ActiveTab is not null,
            () => { workbench.Editor.Group.ActiveTab!.Content = "mine\n"; },
            () => Delete(Path),
            () => workbench.Editor.Group.ActiveTab!.DiskMarker == DiskState.Gone,
            () => host.App.InjectKey(Key.S.WithCtrl),
            () => _fs.File.Exists(_fs.Path.GetFullPath(Path)),
            () => { title = workbench.Editor.Group.ActiveTab!.Title; });

        Assert.Equal("mine\n", _fs.File.ReadAllText(_fs.Path.GetFullPath(Path)));
        Assert.Equal("a.txt", title);
        Assert.Empty(workbench.SubViews.OfType<ConfirmView>());
    }

    private void Delete(string path)
    {
        var full = _fs.Path.GetFullPath(path);
        _fs.File.Delete(full);
        _fs.Watchers.For(_fs.Path.GetDirectoryName(full)!).Raise(WatcherChangeTypes.Deleted, full);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        _fs.AddFile(Path, new MockFileData("one\n"));
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench)
    {
        var commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, fileSystem: _fs);
    }
}
