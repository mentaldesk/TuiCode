using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Unsaved edits come back after TuiCode ends unexpectedly (#579). Boots a TG Application — serialised (#77).
public class UnsavedEditsHostTests : StaticConfigurationTest
{
    private const string Recovery = "/home/.tui/recovery";

    private static readonly RecoveryOwner Killed = new(101, new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc));
    private static readonly RecoveryOwner Next = new(103, new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc));

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly WorkspaceStateStore _workspaces;

    public UnsavedEditsHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("one\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("bravo\n"));
        _fs.AddFile("/work/c.txt", new MockFileData("charlie\n"));
        _workspaces = new WorkspaceStateStore(_fs, "/state.json");
    }

    [Fact]
    public async Task The_status_bar_counts_the_files_whose_edits_came_back()
    {
        var killed = BuildWorkbench(Killed);
        killed.OpenFolder(_fs.DirectoryInfo.New("/work"));
        foreach (var path in new[] { "/work/a.txt", "/work/b.txt", "/work/c.txt" })
            killed.OpenFile(_fs.FileInfo.New(path));
        foreach (var tab in killed.Editor.Group.Tabs.Where(t => t.File.Name != "b.txt"))
            tab.Content = "mine\n";
        killed.UnsavedEdits!.Flush();

        using var workbench = BuildWorkbench(Next);
        using var host = BuildHost(workbench);
        string? message = null;

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New("/work")),
            () => workbench.StatusBar.Message.StartsWith('⟲'),
            () => { message = workbench.StatusBar.Message; });

        Assert.Equal("⟲ Recovered unsaved edits in 2 files", message);
    }

    [Fact]
    public async Task A_hangup_keeps_edits_typed_just_before_it()
    {
        using var workbench = BuildWorkbench(Killed);
        using var host = BuildHost(workbench);
        Task? signal = null;

        await HostSteps.Run(host,
            () =>
            {
                workbench.OpenFolder(_fs.DirectoryInfo.New("/work"));
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = "mine\n";
            },
            () => { signal = Task.Run(() => host.KeepUnsavedEdits(null)); },
            () => signal!.IsCompleted);

        Assert.Single(_fs.Directory.GetFiles(Recovery));
    }

    [Fact]
    public async Task Quitting_without_saving_leaves_nothing_to_recover()
    {
        using var workbench = BuildWorkbench(Killed);
        using var host = BuildHost(workbench);

        await HostSteps.RunUntilQuit(host,
            () =>
            {
                workbench.OpenFolder(_fs.DirectoryInfo.New("/work"));
                workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));
                workbench.Editor.Group.ActiveTab!.Content = "mine\n";
                workbench.UnsavedEdits!.Flush();
            },
            () => host.App.InjectKey(Key.Q.WithCtrl),
            () => Confirm(workbench) is not null,
            () =>
            {
                if (Confirm(workbench)?.FocusedChoice == "Don't save") return true;
                host.App.InjectKey(Key.Tab);
                return false;
            },
            () => host.App.InjectKey(Key.Enter));

        Assert.Empty(_fs.Directory.GetFiles(Recovery));
    }

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private Workbench.Workbench BuildWorkbench(RecoveryOwner owner) =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _workspaces,
            recovery: new RecoveryStore(_fs, Recovery, owner, _ => false));

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
