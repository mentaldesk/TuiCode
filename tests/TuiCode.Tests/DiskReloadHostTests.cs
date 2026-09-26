using Terminal.Gui.Drivers;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// The note a reload leaves in the status bar (#269): once for the tab you're looking at, never for a
// background one. Boots a TG Application — serialised (#77).
public class DiskReloadHostTests : StaticConfigurationTest
{
    private const string Note = "⟳ Reloaded a.txt — changed on disk";

    private readonly WatchableFileSystem _fs = new();

    [Fact]
    public async Task Reloading_the_tab_you_are_looking_at_says_so_once()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";
        var position = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.ActiveTab is not null,
            () => { workbench.Editor.Group.ActiveTab!.MoveCursor(1, 2); Change("/work/a.txt", "alpha\nbravo\ncharlie\n"); },
            () => workbench.StatusBar.DisplayedText.StartsWith(Note, StringComparison.Ordinal),
            () => { said = workbench.StatusBar.DisplayedText; position = workbench.StatusBar.DisplayedPosition; });

        Assert.StartsWith(Note, said);
        Assert.Equal("Ln 2, Col 3", position);
        Assert.Equal("alpha\nbravo\ncharlie\n", workbench.Editor.Group.ActiveTab!.Content.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task A_background_tab_reloads_with_nothing_on_screen()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var said = "";

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.Editor.Group.Tabs.Count == 1,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")),
            () => workbench.Editor.Group.Tabs.Count == 2,
            () => Change("/work/a.txt", "from the other branch\n"),
            () => Behind(workbench).Content.ReplaceLineEndings("\n") == "from the other branch\n",
            () => { said = workbench.StatusBar.DisplayedText; });

        Assert.DoesNotContain("Reloaded", said);
        Assert.EndsWith("b.txt", Behind(workbench).File.FileSystem.Path.GetFileName(workbench.Editor.Group.ActiveTab!.File.FullName));
    }

    private static TuiCode.Editor.EditorTab Behind(Workbench.Workbench workbench) =>
        workbench.Editor.Group.Tabs.Single(t => t.File.Name == "a.txt");

    private void Change(string path, string content)
    {
        var full = _fs.Path.GetFullPath(path);
        _fs.File.WriteAllText(full, content);
        _fs.Watchers.For(_fs.Path.GetDirectoryName(full)!).RaiseChanged(full);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("two\n"));
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
