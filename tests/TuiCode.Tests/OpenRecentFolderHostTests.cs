using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Drives `or` through the host against a mock file system. Boots a TG Application — serialised (#77).
public class OpenRecentFolderHostTests : StaticConfigurationTest
{
    private const string StatePath = "/state.json";

    private readonly MockFileSystem _fs = new();
    private readonly WorkspaceStateStore _store;

    public OpenRecentFolderHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/code/vault/v.txt", new MockFileData("vault\n"));
        _fs.AddFile("/code/zmk/z.txt", new MockFileData("zmk\n"));
        _fs.AddFile("/elsewhere/new/n.txt", new MockFileData("new\n"));
        _store = new WorkspaceStateStore(_fs, StatePath);
        _store.Save(Full("/code/zmk"), new WorkspaceState([Full("/code/zmk/z.txt")], Full("/code/zmk/z.txt")));
        _store.Save(Full("/code/gone"), new WorkspaceState([], null));
        _store.Save(Full("/code/vault"), new WorkspaceState([Full("/code/vault/v.txt")], Full("/code/vault/v.txt")));
        _store.Save(Full("/work"), new WorkspaceState([], null));
    }

    [Fact]
    public async Task Or_lists_the_other_folders_that_still_exist_most_recent_first()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.O),
            () => host.App.InjectKey(Key.R),
            () => Picker(workbench) is not null,
            () => { rows = Picker(workbench)!.VisibleItems; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(["vault", "zmk"], rows);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task Enter_switches_to_the_selected_folder_and_brings_its_tabs_back()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => Type(host, "ZM"),
            () => Picker(workbench)!.VisibleItems is ["zmk"],
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/code/zmk"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal([Full("/code/zmk/z.txt")], workbench.Editor.Group.Tabs.Select(t => t.File.FullName));
    }

    [Fact]
    public async Task Down_moves_the_selection_while_the_filter_keeps_focus()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => host.App.InjectKey(Key.CursorDown),
            () => Picker(workbench)!.SelectedItem == 1,
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/code/zmk"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Fact]
    public async Task Esc_closes_the_picker_and_leaves_the_workspace_and_history_alone()
    {
        var before = _fs.File.ReadAllText(StatePath);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => Type(host, "va"),
            () => Picker(workbench)!.VisibleItems is ["vault"],
            () => host.App.InjectKey(Key.Esc),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(before, _fs.File.ReadAllText(StatePath));
    }

    [Fact]
    public async Task With_no_other_folders_it_says_so_rather_than_opening_an_empty_picker()
    {
        _fs.File.Delete(StatePath);
        _store.Save(Full("/work"), new WorkspaceState([], null));
        _store.Save(Full("/code/gone"), new WorkspaceState([], null));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => workbench.StatusBar.DisplayedText == "No other folders in your history yet.");

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_typed_path_is_offered_as_a_single_row_and_Enter_opens_it_and_remembers_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> rows = [];
        var hint = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => Type(host, "/elsewhere/new/"),
            () => Picker(workbench)!.VisibleItems is ["Open /elsewhere/new/"],
            () => { rows = Picker(workbench)!.VisibleItems; hint = Picker(workbench)!.HintText; },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(["Open /elsewhere/new/"], rows);
        Assert.Equal("Type to filter · Enter open this path · Esc cancel", hint);
        Assert.Equal(Full("/elsewhere/new"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(Full("/elsewhere/new"), workbench.RecentFolders[0]);
    }

    [Fact]
    public async Task A_leading_tilde_is_the_home_directory()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands,
            new FakeEnvironment().SetFolder(Environment.SpecialFolder.UserProfile, Full("/elsewhere")));

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => Type(host, "~/new"),
            () => Picker(workbench)!.VisibleItems is ["Open ~/new"],
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/elsewhere/new"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Theory]
    [InlineData("/code/nope")]
    [InlineData("/code/zmk/z.txt")]
    public async Task A_path_that_is_not_a_folder_says_so_in_the_dialog_until_the_filter_changes(string path)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var status = "";

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Picker(workbench) is not null,
            () => Type(host, path),
            () => Picker(workbench)!.Filter == path,
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench)!.Status != "",
            () => { status = Picker(workbench)!.Status; },
            () => host.App.InjectKey(Key.Backspace),
            () => Picker(workbench)!.Status == "");

        Assert.Equal($"No such folder: {path}", status);
        Assert.NotNull(Picker(workbench));
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static RecentFolderPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<RecentFolderPickerView>().SingleOrDefault();

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
