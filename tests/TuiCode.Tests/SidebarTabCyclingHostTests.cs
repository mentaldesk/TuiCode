using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Ctrl+PgUp/PgDn switch the sidebar's tabs while it has the keys. Boots a TG Application — serialised (#77).
public class SidebarTabCyclingHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();

    [Fact]
    public async Task Ctrl_PageDown_in_the_sidebar_shows_its_next_tab_and_leaves_the_editor_tabs_alone()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("a"));
        _fs.AddFile("/work/b.txt", new MockFileData("b"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.OpenFile(_fs.FileInfo.New("/work/b.txt")),
            () => _commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.PageDown.WithCtrl),
            () => workbench.StatusBar.DisplayedFocus == "Find");

        Assert.Equal(SidebarTab.Find, workbench.Sidebar.ActiveTab);
        Assert.Equal("b.txt", workbench.Editor.Group.ActiveTab!.File.Name);
    }

    [Fact]
    public async Task Ctrl_PageUp_from_the_first_sidebar_tab_wraps_to_the_last()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => _commands.TryExecute(CommandIds.ShowExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.PageUp.WithCtrl),
            () => workbench.StatusBar.DisplayedFocus == "Review");

        Assert.Equal(SidebarTab.Review, workbench.Sidebar.ActiveTab);
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        _fs.AddDirectory("/work");
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(), new InMemorySettingsService(),
            driverName: DriverRegistry.Names.ANSI, fileSystem: _fs);
}
