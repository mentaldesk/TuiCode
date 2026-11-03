using Terminal.Gui.Drivers;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Menus;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Reveal in explorer (#652). Boots a TG Application — serialised (#77).
public class RevealInExplorerHostTests : StaticConfigurationTest
{
    private const string Target = "/work/src/deep/nested/target.cs";

    private readonly MockFileSystem _fs = new();
    private readonly CommandService _commands = new();
    private readonly KeybindingService _keybindings;

    public RevealInExplorerHostTests()
    {
        _keybindings = new KeybindingService(_commands);
        for (var i = 0; i < 40; i++) _fs.AddFile($"/work/a{i:00}.txt", new MockFileData("a\n"));
        _fs.AddFile(Target, new MockFileData("class Target;\n"));
        _fs.AddFile("/work/src/deep/nested/neighbour.cs", new MockFileData("class Neighbour;\n"));
        _fs.AddFile("/work/src/other/b.cs", new MockFileData("b\n"));
        _fs.AddFile("/elsewhere/notes.md", new MockFileData("n\n"));
    }

    [Fact]
    public async Task Ctrl_G_F_opens_the_folders_down_to_the_file_selects_it_in_view_and_focuses_the_tree()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var explorer = workbench.Sidebar.Explorer;

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 30),
            () => Open(workbench, Target),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.G.WithCtrl),
            () => host.App.InjectKey(Key.F),
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal(Full(Target), explorer.SelectedObject?.FullName);
        foreach (var folder in new[] { "/work/src", "/work/src/deep", "/work/src/deep/nested" })
            Assert.True(explorer.IsExpanded(Node(explorer, folder)), folder);
        Assert.False(explorer.IsExpanded(Node(explorer, "/work/src/other")));
        Assert.InRange(explorer.GetObjectRow(explorer.SelectedObject!) ?? -1, 0, explorer.Viewport.Height - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task It_shows_the_Explorer_when_the_sidebar_is_hidden_or_on_another_tab(bool hidden)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => HostSteps.PinScreenSize(host, 100, 30),
            () =>
            {
                Open(workbench, Target);
                workbench.Sidebar.ShowTab(SidebarTab.Find);
                if (hidden) workbench.SetSidebarVisible(false);
            },
            () => workbench.Editor.Group.ActiveTab is not null,
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        var explorer = workbench.Sidebar.Explorer;
        Assert.True(workbench.IsSidebarVisible);
        Assert.Equal(SidebarTab.Explorer, workbench.Sidebar.ActiveTab);
        Assert.Equal(Full(Target), explorer.SelectedObject?.FullName);
        Assert.InRange(explorer.GetObjectRow(explorer.SelectedObject!) ?? -1, 0, explorer.Viewport.Height - 1);
    }

    [Fact]
    public async Task A_diff_tab_reveals_its_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, Target);
                group.ActiveTab!.Content = "class Changed;\n";
                group.CompareToSaved(group.ActiveTab!);
            },
            () => group.ActiveDiffTab is not null,
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal(Full(Target), workbench.Sidebar.Explorer.SelectedObject?.FullName);
    }

    [Fact]
    public async Task Enter_on_a_neighbour_opens_it_left_goes_to_the_folder_and_Esc_returns_to_the_editor()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var explorer = workbench.Sidebar.Explorer;
        string? parent = null;

        await HostSteps.Run(host,
            () => Open(workbench, Target),
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.CursorLeft),
            () => explorer.SelectedObject?.Name == "nested",
            () => { parent = explorer.SelectedObject!.FullName; _commands.TryExecute(CommandIds.RevealInExplorer); },
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.CursorUp),
            () => explorer.SelectedObject?.Name == "neighbour.cs",
            () => host.App.InjectKey(Key.Enter),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.Esc),
            () => workbench.StatusBar.DisplayedFocus == "Editor");

        Assert.Equal(Full("/work/src/deep/nested"), parent);
        Assert.Equal("neighbour.cs", workbench.Editor.Group.ActiveTab?.File.Name);
    }

    [Theory]
    [InlineData("/elsewhere/notes.md", "notes.md isn't in this folder")]
    [InlineData("/work/src/other/b.cs", "b.cs isn't on disk")]
    public async Task A_file_it_cannot_reveal_says_why_and_leaves_the_explorer_alone(string path, string message)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var explorer = workbench.Sidebar.Explorer;
        IReadOnlyList<string> expanded = [];

        await HostSteps.Run(host,
            () =>
            {
                Open(workbench, path);
                explorer.SelectedObject = Node(explorer, "/work/a05.txt");
                expanded = [.. explorer.ExpandedFolders];
                _fs.RemoveFile(Full("/work/src/other/b.cs"));
            },
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.Message == message);

        Assert.Equal(Full("/work/a05.txt"), explorer.SelectedObject?.FullName);
        Assert.Equal(expanded, explorer.ExpandedFolders);
        Assert.Equal("Editor", workbench.StatusBar.DisplayedFocus);
    }

    [Fact]
    public async Task It_is_offered_only_while_the_active_tab_is_a_file()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var group = workbench.Editor.Group;
        var offered = new List<bool>();

        await HostSteps.Run(host,
            () => offered.Add(_commands.IsEnabled(CommandIds.RevealInExplorer)),
            () => Open(workbench, Target),
            () => group.ActiveTab is not null,
            () => { offered.Add(_commands.IsEnabled(CommandIds.RevealInExplorer)); group.OpenDocument(_fs.FileInfo.New(Full("/work/Settings")), "{}", "Settings"); },
            () => group.ActiveDocumentTab is not null,
            () => offered.Add(_commands.IsEnabled(CommandIds.RevealInExplorer)));

        Assert.Equal([false, true, false], offered);
    }

    [Fact]
    public async Task The_revealed_file_is_kept_with_the_workspace_so_its_folders_reopen()
    {
        var store = new WorkspaceStateStore(_fs, "/state.json");
        store.Save(Full("/work"), new WorkspaceState([Full(Target)], Full(Target)));
        using var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), store);
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => workbench.OpenFolder(_fs.DirectoryInfo.New(Full("/work"))),
            () => workbench.Editor.Group.ActiveTab is not null,
            () => _commands.TryExecute(CommandIds.RevealInExplorer),
            () => workbench.StatusBar.DisplayedFocus == "Explorer");

        Assert.Equal(new ExplorerRow(Full(Target), false), store.Load(Full("/work"))!.ExplorerSelected);
    }

    [Fact]
    public void Gf_is_Reveal_in_explorer_on_Ctrl_G_F_after_Show_file_history_in_the_View_menu()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        Assert.Equal("gf", CommandMnemonics.For(CommandIds.RevealInExplorer));
        Assert.Equal(CommandScope.Global, _commands.ScopeOf(CommandIds.RevealInExplorer));
        var binding = Assert.Single(_keybindings.Bindings, b => b.CommandId == CommandIds.RevealInExplorer);
        Assert.Equal("Ctrl+G f", binding.Display);
        var view = CommandMenu.Layout.Single(m => m.Title == "_View").Ids;
        Assert.Equal(CommandIds.RevealInExplorer, view[Array.IndexOf(view, CommandIds.ToggleFileHistory) + 1]);
        Assert.Equal('x', CommandMenu.Letters[CommandIds.RevealInExplorer]);
    }

    private IFileSystemInfo Node(FileExplorerView explorer, string path)
    {
        IFileSystemInfo node = explorer.Root!;
        while (node.FullName != Full(path))
            node = explorer.GetChildren(node).Single(c => FilePaths.IsSameOrUnder(Full(path), c.FullName));
        return node;
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private void Open(Workbench.Workbench workbench, string path) => workbench.OpenFile(_fs.FileInfo.New(Full(path)));

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(Full("/work")));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, _keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI);
}
