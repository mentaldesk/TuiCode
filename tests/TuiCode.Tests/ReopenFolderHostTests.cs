using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Configuration;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Reopening a folder puts back the sidebar and where the keys were (#645). Boots a TG Application — serialised (#77).
public class ReopenFolderHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly CommandService _commands = new();
    private readonly WorkspaceStateStore _store;

    public ReopenFolderHostTests()
    {
        _store = new WorkspaceStateStore(_fs, "/state.json");
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("a\n"));
        _fs.AddFile("/work/src/b.txt", new MockFileData("b\n"));
        _git.Root = Full("/work");
        _git.FileCommits[Full("/work/a.txt")] = [new GitFileCommit(new string('1', 40), "Ada", DateTimeOffset.Now, "Add a", "a.txt", null, false)];
    }

    [Fact]
    public async Task A_folder_saved_with_the_keys_in_the_explorer_opens_with_them_on_the_saved_row()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt"))
        {
            Sidebar = SidebarTab.Explorer,
            Focus = WorkspaceFocus.Sidebar,
            ExplorerSelected = new ExplorerRow(Full("/work/src/b.txt"), Folder: false),
        });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => workbench.StatusBar.DisplayedFocus == "Explorer" && workbench.Sidebar.Explorer.HasFocus);

        Assert.Equal(Full("/work/src/b.txt"), workbench.Sidebar.Explorer.SelectedObject?.FullName);
        Assert.Equal("a.txt", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Fact]
    public async Task A_folder_saved_with_the_keys_in_Find_opens_with_the_cursor_in_its_query()
    {
        Save(new WorkspaceState([], null) { Sidebar = SidebarTab.Find, Focus = WorkspaceFocus.Sidebar });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => workbench.StatusBar.DisplayedFocus == "Find" && workbench.Sidebar.Search.InputsHaveFocus);

        Assert.Equal(SidebarTab.Find, workbench.Sidebar.ActiveTab);
        Assert.False(workbench.Sidebar.Search.ReplacementHasFocus);
    }

    [Fact]
    public async Task A_folder_saved_with_the_keys_in_AI_assistance_opens_with_them_there()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt"))
        {
            Sidebar = SidebarTab.Explorer,
            Assistant = true,
            Focus = WorkspaceFocus.Assistant,
        });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 160, 30),
            () => workbench.StatusBar.DisplayedFocus == "AI assistance");

        Assert.True(workbench.IsAssistantShowing);
    }

    [Fact]
    public async Task A_folder_saved_with_the_keys_in_History_opens_with_them_there_once_it_lists_the_file()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt"))
        {
            Sidebar = SidebarTab.Explorer,
            History = true,
            Focus = WorkspaceFocus.History,
        });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => workbench.StatusBar.DisplayedFocus == "History" && workbench.History.ListHasFocus);
    }

    [Fact]
    public async Task A_folder_saved_with_the_keys_in_a_group_that_is_gone_opens_with_them_in_the_first()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt"))
        {
            Second = new WorkspaceGroup([Full("/work/gone.txt")], Full("/work/gone.txt")),
            SecondFocused = true,
            Sidebar = SidebarTab.Explorer,
            ExplorerSelected = new ExplorerRow(Full("/work/src/b.txt"), Folder: false),
        });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);
        var first = workbench.Editor.Groups.First;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => first.ActiveTab?.ContentHasFocus == true && workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.False(workbench.Editor.Groups.IsSplit);
    }

    [Fact]
    public async Task A_folder_saved_with_the_sidebar_hidden_on_a_tab_opens_that_way()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt")) { Sidebar = SidebarTab.Usages, SidebarHidden = true });
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => workbench.Editor.Group.ActiveTab?.ContentHasFocus == true);

        Assert.False(workbench.IsSidebarVisible);
        Assert.Equal(SidebarTab.Usages, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task A_folder_saved_before_the_sidebar_was_kept_opens_as_it_did()
    {
        Save(new WorkspaceState([Full("/work/a.txt")], Full("/work/a.txt")));
        using var workbench = BuildWorkbench();
        using var host = Start(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 120, 30),
            () => workbench.Editor.Group.ActiveTab?.ContentHasFocus == true);

        Assert.True(workbench.IsSidebarVisible);
        Assert.Equal(SidebarTab.Explorer, workbench.Sidebar.ActiveTab);
    }

    [Fact]
    public async Task Switching_the_sidebar_tab_or_hiding_it_is_saved_at_once()
    {
        using var workbench = BuildWorkbench();
        workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work")));
        using var host = BuildHost(workbench);
        WorkspaceState? switched = null;
        WorkspaceState? hidden = null;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New(Full("/work/a.txt"))),
            () => _commands.TryExecute(CommandIds.FindGlobally),
            () => { switched = Load(); },
            () => _commands.TryExecute(CommandIds.ToggleSidebar),
            () => { hidden = Load(); });

        Assert.Equal(SidebarTab.Find, switched?.Sidebar);
        Assert.Equal(WorkspaceFocus.Sidebar, switched?.Focus);
        Assert.True(hidden?.SidebarHidden);
        Assert.Equal(WorkspaceFocus.Editor, hidden?.Focus);
    }

    [Fact]
    public async Task Quitting_keeps_the_selected_row_and_where_the_keys_were()
    {
        using (var workbench = BuildWorkbench())
        using (var host = BuildHost(workbench))
        {
            workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work")));
            await HostSteps.Run(host,
                () => _commands.TryExecute(CommandIds.ShowExplorer),
                () => workbench.StatusBar.DisplayedFocus == "Explorer",
                () => workbench.Sidebar.Explorer.Restore(Full("/work/src"), folder: true));
        }

        Assert.Equal(WorkspaceFocus.Sidebar, Load().Focus);
        Assert.Equal(new ExplorerRow(Full("/work/src"), Folder: true), Load().ExplorerSelected);
    }

    private void Save(WorkspaceState state) => _store.Save(Full("/work"), state);

    private WorkspaceState Load() => _store.Load(Full("/work"))!;

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private Workbench.Workbench BuildWorkbench() =>
        new(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), _store);

    private WorkbenchHost Start(Workbench.Workbench workbench)
    {
        var host = BuildHost(workbench);
        host.OpenWhenRunning(new StartupTarget(_fs.DirectoryInfo.New(Full("/work")), []));
        return host;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, fileSystem: _fs);
}
