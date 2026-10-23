using MentalDesk.Tui.Chrome;
using Terminal.Gui.Drivers;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Replies, news and the resting path on the status bar (#592). Boots a TG Application — serialised (#77).
public class StatusMessagesHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();

    public StatusMessagesHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
    }

    [Fact]
    public async Task Ctrl_S_replies_with_the_file_saved_and_the_next_key_puts_the_path_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var path = _fs.Path.GetFullPath("/work/a.txt");
        var said = "";
        MessageKind? kind = null;

        await HostSteps.Run(host,
            () =>
            {
                var tab = Open(workbench, "/work/a.txt");
                tab.Content = "alpha!\n";
                tab.FocusContent();
            },
            () => host.App.InjectKey(Key.S.WithCtrl),
            () =>
            {
                said = workbench.StatusBar.DisplayedText;
                kind = workbench.StatusBar.Bar.Current?.Kind;
                host.App.InjectKey(Key.CursorRight);
            },
            () => { });

        Assert.Equal($"Saved: {path}", said);
        Assert.Equal(MessageKind.Reply, kind);
        Assert.Equal(path, workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_resting_text_follows_the_active_tab_and_is_TuiCode_with_none_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        List<string> shown = [];

        await HostSteps.Run(host,
            () => { Open(workbench, "/work/a.txt"); },
            () => { Open(workbench, "/work/b.txt"); },
            () =>
            {
                shown.Add(workbench.StatusBar.DisplayedText);
                workbench.Editor.Group.Focus(_fs.Path.GetFullPath("/work/a.txt"));
            },
            () =>
            {
                shown.Add(workbench.StatusBar.DisplayedText);
                workbench.CloseAllTabs();
            },
            () => shown.Add(workbench.StatusBar.DisplayedText));

        Assert.Equal([_fs.Path.GetFullPath("/work/b.txt"), _fs.Path.GetFullPath("/work/a.txt"), "TuiCode"], shown);
    }

    private TuiCode.Editor.EditorTab Open(Workbench.Workbench workbench, string path)
    {
        workbench.OpenFile(_fs.FileInfo.New(path));
        return workbench.Editor.Group.ActiveTab!;
    }

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
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
