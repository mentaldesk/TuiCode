using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Navigation;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Workspace;

namespace TuiCode.Tests;

// Switching folders with unsaved edits asks first (#412). Boots a TG Application — serialised (#77).
public class SwitchFolderDirtyHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly FakeGitHubCli _gitHub = new();
    private readonly WorkspaceStateStore _store;
    private readonly CommandService _commands = new();

    public SwitchFolderDirtyHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\n"));
        _fs.AddFile("/work/b.txt", new MockFileData("one\n"));
        _fs.AddFile("/code/feature/f.txt", new MockFileData("feature\n"));
        _store = new WorkspaceStateStore(_fs, "/state.json");
        _store.Save(Full("/code/feature"), new WorkspaceState([], null));
        _store.Save(Full("/work"), new WorkspaceState([], null));
        _git.Root = Full("/work");
        _git.ListedWorktrees =
        [
            new GitWorktree(Full("/work"), "main", "aaaaaaa111"),
            new GitWorktree(Full("/code/feature"), "feat/thing", "bbbbbbb222"),
        ];
        _gitHub.OpenPullRequests = [new GitHubPullRequestSummary(132, "Command scopes", "octocat", "fix-scopes")];
    }

    public static TheoryData<string> FolderSwitches =>
    [
        CommandIds.Open, CommandIds.OpenPullRequest, CommandIds.OpenRecentFolder, CommandIds.OpenWorktree, CommandIds.OpenFilePath,
    ];

    [Theory]
    [MemberData(nameof(FolderSwitches))]
    public async Task Dirty_tabs_are_asked_about_before_the_picker_opens(string command)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = "";
        var focused = "";
        var pickerShown = false;

        await HostSteps.Run(host,
            () => Edit(workbench, "/work/a.txt", "mine\n"),
            () => _commands.TryExecute(command),
            () => Confirm(workbench) is not null,
            () =>
            {
                (asked, focused) = (Message(Confirm(workbench)!), Confirm(workbench)!.FocusedChoice!);
                pickerShown = Picker(workbench) is not null;
            });

        Assert.Equal(WorkbenchHost.UnsavedChanges(["a.txt"], "Save them before opening another folder?"),
            asked.ReplaceLineEndings("\n"));
        Assert.Equal("Cancel", focused);
        Assert.False(pickerShown);
    }

    [Theory]
    [MemberData(nameof(FolderSwitches))]
    public async Task Nothing_dirty_goes_straight_to_the_picker(string command)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var asked = false;
        workbench.SubViewAdded += (_, e) => asked |= e.SubView is ConfirmView;

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => _commands.TryExecute(command),
            () => Picker(workbench) is not null);

        Assert.False(asked);
    }

    [Theory]
    [InlineData(false)] // Enter lands on the focused Cancel
    [InlineData(true)]
    public async Task Cancelling_opens_nothing_and_keeps_the_tab_dirty(bool escape)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pickerShown = false;

        await HostSteps.Run(host,
            () => Edit(workbench, "/work/a.txt", "mine\n"),
            () => _commands.TryExecute(CommandIds.OpenWorktree),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(escape ? Key.Esc : Key.Enter),
            () => Confirm(workbench) is null,
            () => { pickerShown = Picker(workbench) is not null; });

        Assert.False(pickerShown);
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
        Assert.Equal(Full("/work"), workbench.Sidebar.Explorer.Root?.FullName);
    }

    [Theory]
    [MemberData(nameof(FolderSwitches))]
    public async Task Dont_save_then_backing_out_of_the_picker_keeps_the_tab_open_and_dirty(string command)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Edit(workbench, "/work/a.txt", "mine\n"),
            () => _commands.TryExecute(command),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Don't save"),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is not null,
            () => host.App.InjectKey(Key.Esc),
            () => Picker(workbench) is null);

        Assert.Equal(["a.txt"], workbench.Editor.Group.Tabs.Where(t => t.IsDirty).Select(t => t.File.Name));
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Dont_save_then_opening_a_folder_closes_the_tabs_without_saving()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () => Edit(workbench, "/work/a.txt", "mine\n"),
            () => _commands.TryExecute(CommandIds.OpenFilePath),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Don't save"),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is not null,
            () => Type(host, Full("/code/feature")),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Equal(Full("/code/feature"), workbench.Sidebar.Explorer.Root?.FullName);
        Assert.DoesNotContain(workbench.Editor.Group.Tabs, t => t.File.Name == "a.txt");
        Assert.Equal("one\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    [Fact]
    public async Task Save_all_writes_every_dirty_tab_then_opens_the_picker()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);

        await HostSteps.Run(host,
            () =>
            {
                Edit(workbench, "/work/a.txt", "mine a\n");
                Edit(workbench, "/work/b.txt", "mine b\n");
            },
            () => _commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is not null);

        Assert.Equal("mine a\n", _fs.File.ReadAllText("/work/a.txt"));
        Assert.Equal("mine b\n", _fs.File.ReadAllText("/work/b.txt"));
        Assert.DoesNotContain(workbench.Editor.Group.Tabs, t => t.IsDirty);
    }

    [Fact]
    public async Task A_failed_save_stops_before_the_picker()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pickerShown = false;

        await HostSteps.Run(host,
            () =>
            {
                Edit(workbench, "/work/a.txt", "mine\n");
                _fs.File.SetAttributes("/work/a.txt", FileAttributes.ReadOnly);
            },
            () => _commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null,
            () => { pickerShown = Picker(workbench) is not null; });

        Assert.False(pickerShown);
        Assert.True(workbench.Editor.Group.ActiveTab!.IsDirty);
    }

    [Fact]
    public async Task A_conflict_not_overwritten_stops_before_the_picker()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench);
        var pickerShown = false;

        await HostSteps.Run(host,
            () =>
            {
                Edit(workbench, "/work/a.txt", "mine\n");
                _fs.File.WriteAllText("/work/a.txt", "theirs\n");
            },
            () => _commands.TryExecute(CommandIds.OpenRecentFolder),
            () => Confirm(workbench) is not null,
            Reach(host, workbench, "Save all"),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench)?.Title == "File changed on disk",
            () => host.App.InjectKey(Key.Esc),
            () => Confirm(workbench) is null,
            () => { pickerShown = Picker(workbench) is not null; });

        Assert.False(pickerShown);
        Assert.Equal("theirs\n", _fs.File.ReadAllText("/work/a.txt"));
    }

    // Cancel starts focused, so Tab round the row until the wanted button has it.
    private static Func<bool> Reach(WorkbenchHost host, Workbench.Workbench workbench, string label) => () =>
    {
        if (Confirm(workbench)?.FocusedChoice == label) return true;
        host.App.InjectKey(Key.Tab);
        return false;
    };

    private void Edit(Workbench.Workbench workbench, string path, string content)
    {
        workbench.OpenFile(_fs.FileInfo.New(path));
        workbench.Editor.Group.ActiveTab!.Content = content;
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private static View? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.SingleOrDefault(v => v is OpenView or PullRequestPickerView or RecentFolderPickerView
            or WorktreePickerView or PathPromptView);

    private static string Message(ConfirmView view) => view.SubViews.OfType<Label>().First().Text;

    private Workbench.Workbench BuildWorkbench()
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart(), _store);
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench) =>
        new(workbench, _commands, new KeybindingService(_commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
}
