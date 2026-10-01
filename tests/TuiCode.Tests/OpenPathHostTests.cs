using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Drives `opa` through the host against a mock file system. Boots a TG Application — serialised (#77).
public class OpenPathHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceStateStore _store;

    public OpenPathHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/elsewhere/new/n.txt", new MockFileData("new\n"));
        _store = new WorkspaceStateStore(_fs, "/state.json");
    }

    [Fact]
    public async Task Opa_opens_the_typed_folder_and_remembers_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.O),
            () => host.App.InjectKey(Key.P),
            () => host.App.InjectKey(Key.A),
            () => Prompt(workbench) is not null,
            () => Type(host, "/elsewhere/new/"),
            () => Prompt(workbench)!.Path == "/elsewhere/new/",
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench) is null);

        Assert.Equal(Full("/elsewhere/new"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(Full("/elsewhere/new"), workbench.RecentFolders[0]);
    }

    [Fact]
    public async Task A_leading_tilde_is_the_home_folder()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands,
            new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Full("/elsewhere")));

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPath),
            () => Prompt(workbench) is not null,
            () => Type(host, "~/new"),
            () => Prompt(workbench)!.Path == "~/new",
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench) is null);

        Assert.Equal(Full("/elsewhere/new"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Theory]
    [InlineData("/elsewhere/nope")]
    [InlineData("/elsewhere/new/n.txt")]
    [InlineData("elsewhere/new")]
    public async Task A_path_that_is_not_a_folder_says_so_until_the_path_changes(string path)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var error = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenPath),
            () => Prompt(workbench) is not null,
            () => Type(host, path),
            () => Prompt(workbench)!.Path == path,
            () => host.App.InjectKey(Key.Enter),
            () => Prompt(workbench)!.Error != "",
            () => { error = Prompt(workbench)!.Error; },
            () => host.App.InjectKey(Key.Backspace),
            () => Prompt(workbench)!.Error == "");

        Assert.Equal($"No such folder: {path}", error);
        Assert.NotNull(Prompt(workbench));
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static PathPromptView? Prompt(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<PathPromptView>().SingleOrDefault();

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

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
