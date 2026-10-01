using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Drives `ow` through the host against a fake git. Boots a TG Application — serialised (#77).
public class OpenWorktreeHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();

    public OpenWorktreeHostTests()
    {
        _fs.AddDirectory("/code/main/.git");
        _fs.AddFile("/code/main/sub/a.txt", new MockFileData("alpha\n"));
        _fs.AddFile("/code/feature/b.txt", new MockFileData("bravo\n"));
        _fs.AddFile("/code/pr-7/c.txt", new MockFileData("charlie\n"));
        _git.Root = Full("/code/main");
        _git.ListedWorktrees =
        [
            new GitWorktree(Full("/code/main"), "main", "aaaaaaa111"),
            new GitWorktree(Full("/code/feature"), "feat/thing", "bbbbbbb222"),
            new GitWorktree(Full("/code/pr-7"), null, "3f9c2e1abc"),
        ];
    }

    [Fact]
    public async Task Ow_lists_the_other_worktrees_leaving_out_the_one_whose_subfolder_is_open()
    {
        using var workbench = BuildWorkbench("/code/main/sub");
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> rows = [];
        var header = "";

        await HostSteps.Run(host,
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.O),
            () => host.App.InjectKey(Key.W),
            () => Picker(workbench) is { Header.Length: > 0 },
            () => { rows = Picker(workbench)!.VisibleItems; header = Picker(workbench)!.Header; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(["feature", "pr-7"], rows);
        Assert.Matches(@"^Branch +Worktree +Location$", header);
        Assert.Null(Picker(workbench));
        Assert.Equal(Full("/code/main/sub"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Fact]
    public async Task Typing_filters_by_branch_and_Enter_switches_the_workspace_with_its_tabs_back()
    {
        var store = new WorkspaceStateStore(_fs, "/state.json");
        store.Save(Full("/code/pr-7"), new WorkspaceState([Full("/code/pr-7/c.txt")], Full("/code/pr-7/c.txt")));
        using var workbench = BuildWorkbench("/code/main", store);
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenWorktree),
            () => Picker(workbench) is not null,
            () => Type(host, "DETACHED"),
            () => Picker(workbench)!.VisibleItems is ["pr-7"] && Picker(workbench)!.SelectedItem == 0,
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/code/pr-7"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.Equal(Full("/code/pr-7/c.txt"), workbench.Editor.Group.ActiveTab?.File.FullName);
    }

    [Fact]
    public async Task Outside_a_git_repository_ow_says_so_and_opens_nothing()
    {
        _git.Root = null;
        using var workbench = BuildWorkbench("/code/main");
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenWorktree),
            () => workbench.StatusBar.DisplayedText == "Not in a git repository.");

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_repo_with_no_other_worktrees_says_so_rather_than_opening_an_empty_picker()
    {
        _git.ListedWorktrees = [new GitWorktree(Full("/code/main"), "main", "aaaaaaa111")];
        using var workbench = BuildWorkbench("/code/main");
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenWorktree),
            () => workbench.StatusBar.DisplayedText == "This repo has no other worktrees.");

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task Without_git_ow_says_so_in_the_status_bar_and_opens_nothing()
    {
        _git.Missing = true;
        using var workbench = BuildWorkbench("/code/main");
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenWorktree),
            () => workbench.StatusBar.DisplayedText == "git isn't installed or isn't on PATH");

        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_worktree_listing_git_refuses_is_one_status_bar_line()
    {
        _git.ListWorktreesError = "fatal: not a git repository";
        using var workbench = BuildWorkbench("/code/main");
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.OpenWorktree),
            () => workbench.StatusBar.DisplayedText == "fatal: not a git repository");

        Assert.Null(Picker(workbench));
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static WorktreePickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<WorktreePickerView>().SingleOrDefault();

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private Workbench.Workbench BuildWorkbench(string folder, WorkspaceStateStore? store = null)
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart(), store);
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(folder));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git);
    }
}
