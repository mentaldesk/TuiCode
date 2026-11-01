using MentalDesk.Tui.Paths;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Drives `opa` through the host against a mock file system. Boots a TG Application — serialised (#77).
public class OpenFilePathHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceStateStore _store;

    public OpenFilePathHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/elsewhere/new/n.txt", new MockFileData("new\n"));
        _store = new WorkspaceStateStore(_fs, "/state.json");
    }

    [Fact]
    public async Task Opa_opens_a_folder_picker_at_home_and_Ctrl_Enter_opens_that_folder_and_remembers_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _,
            new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Full("/elsewhere/new")));
        PathPicker? picker = null;
        IReadOnlyList<string> buttons = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.O),
            () => host.App.InjectKey(Key.P),
            () => host.App.InjectKey(Key.A),
            () => (picker = PathPickers.Showing(workbench)) is not null,
            () => { buttons = PathPickers.Buttons(picker!); },
            () => host.App.InjectKey(Key.Enter.WithCtrl),
            () => PathPickers.Showing(workbench) is null);

        Assert.Equal("Open file path", picker!.Title);
        Assert.Equal(Full("/elsewhere/new"), picker.Folder);
        Assert.Equal(["Enter Open", "Ctrl+Enter Use this folder", "Esc Cancel"], buttons);
        Assert.Equal(Full("/elsewhere/new"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(Full("/elsewhere/new"), workbench.RecentFolders[0]);
    }

    [Fact]
    public async Task Esc_leaves_the_folder_open()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenFilePath),
            () => PathPickers.Showing(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => PathPickers.Showing(workbench) is null);

        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _store);
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private static WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands, IEnvironment? environment = null)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), environment: environment, driverName: DriverRegistry.Names.ANSI);
    }
}
