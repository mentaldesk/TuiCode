using Terminal.Gui.Drivers;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The Explorer notices entries appearing and going in an expanded folder by itself (#334).
// Boots a TG Application — serialised (#77).
public class ExplorerWatchHostTests : StaticConfigurationTest
{
    private readonly WatchableFileSystem _fs = new();

    [Fact]
    public async Task A_file_created_outside_appears_in_the_tree_without_a_keypress()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var explorer = workbench.Sidebar.Explorer;

        await HostSteps.Run(host,
            () => Create("/work/b.txt"),
            () => Names(explorer).Contains("b.txt"));

        Assert.Equal(["a.txt", "b.txt"], Names(explorer));
    }

    [Fact]
    public void Closing_the_host_stops_watching_the_explorer()
    {
        using var workbench = BuildWorkbench();
        var host = BuildHost(workbench);
        Assert.NotEmpty(_fs.Watchers.Live);

        host.Dispose();

        Assert.Empty(_fs.Watchers.Live);
    }

    private void Create(string path)
    {
        var full = _fs.Path.GetFullPath(path);
        _fs.AddFile(full, new MockFileData(""));
        foreach (var watcher in _fs.Watchers.At(_fs.Path.GetDirectoryName(full)!)) watcher.Raise(WatcherChangeTypes.Created, full);
    }

    private static string[] Names(FileExplorerView explorer) =>
        [.. explorer.GetChildren(explorer.Root!).Select(c => c.Name)];

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        _fs.AddFile("/work/a.txt", new MockFileData(""));
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
