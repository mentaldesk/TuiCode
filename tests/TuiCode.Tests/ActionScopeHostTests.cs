using TuiCode.Abstractions;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Actions;
using TuiCode.Workbench.Files;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Review;
using TuiCode.Workbench.Services;
using TuiCode.Workbench.Settings;

namespace TuiCode.Tests;

// Ctrl+E offers Global plus the scope the keys were in when it opened, less whatever the workbench has
// switched off (#284). Boots a TG Application — serialised (#77).
public class ActionScopeHostTests : StaticConfigurationTest
{
    private static readonly string[] Editing =
        [
        "Move line up", "Add cursor above", "Toggle column select", "Go to symbol in file", "Go to line:column",
        "Change grammar", "Show document info", "Previous cursor position", "Next cursor position",
    ];

    private static readonly string[] FileCommands =
        ["Delete file or folder", "Move or rename file or folder", "Cut file or folder", "Paste file or folder"];

    private static readonly string[] Comparisons = ["Compare to saved", "Compare to other file", "Compare to revision"];

    // Only offered while the branch's pull request is loaded, which none of the theory's palettes do.
    private static readonly string[] ReviewCommands = ["PR overview", "Submit review", "Create comment"];

    private static readonly string[] NeedAnEditor =
    [
        "Save active editor", "Close active editor", "Go to line:column", "Change grammar", "Toggle gutter",
        "Show document info", "Previous cursor position", "Next cursor position", "Focus editor tab 1",
    ];

    private static readonly string[] Globals =
    [
        "Save active editor", "Close active editor", "Next tab", "Previous tab", "Toggle sidebar",
        "Open settings", "Quit", "Getting Started (help)",
    ];

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    public ActionScopeHostTests()
    {
        _fs.AddDirectory("/work/.git");
        _fs.AddFile("/work/a.txt", new MockFileData("one\ntwo\n"));
    }

    public static TheoryData<string, string, string[], string[]> Palettes => new()
    {
        {
            CommandIds.FocusEditorBody, "Editor", [.. Editing, .. Comparisons],
            [.. FileCommands, .. ReviewCommands, "Focus find results", "Next change"]
        },
        {
            CommandIds.FocusSidebar, "Explorer", FileCommands,
            [.. Editing, .. Comparisons, .. ReviewCommands, "Focus find results", "Next change"]
        },
        {
            CommandIds.FindGlobally, "Find",
            ["Focus find results", "Switch find field", "Replace all globally"],
            [.. Editing, .. FileCommands, .. Comparisons, .. ReviewCommands, "Next change"]
        },
        {
            CommandIds.CompareToSaved, "Diff",
            ["Next change", "Previous change", "Revert change", "Revert all changes in file", "Scroll diff left"],
            [.. Editing, .. FileCommands, .. Comparisons, .. ReviewCommands, "Focus find results"]
        },
        {
            CommandIds.FocusEditorTabStrip, "Tabs", [.. Editing, .. Comparisons],
            [.. FileCommands, .. ReviewCommands, "Focus find results", "Next change"]
        },
        {
            CommandIds.FindInFile, "Find", [.. Editing, .. Comparisons],
            [.. FileCommands, .. ReviewCommands, "Focus find results", "Next change"]
        },
        {
            CommandIds.FocusReview, "Review", [],
            [.. Editing, .. FileCommands, .. Comparisons, .. ReviewCommands, "Focus find results", "Next change"]
        },
    };

    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task The_palette_offers_what_applies_where_it_was_opened(
        string focusCommand, string word, string[] present, string[] absent)
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => { workbench.Editor.Group.ActiveTab!.Content = "one\nTWO\n"; },
            () => commands.TryExecute(focusCommand),
            () => workbench.StatusBar.DisplayedFocus == word,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { labels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.All(Globals, label => Assert.Contains(label, labels));
        Assert.All(present, label => Assert.Contains(label, labels));
        Assert.All(absent, label => Assert.DoesNotContain(label, labels));
    }

    // Opening the palette pushes a modal input scope, so the scope has to be taken as it opens, not read
    // back when a command runs.
    [Fact]
    public async Task The_palette_keeps_the_scope_it_opened_in_when_focus_moves_under_it()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => workbench.Sidebar.Explorer.SetFocus(),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { labels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.Contains("Move line up", labels);
        Assert.DoesNotContain("Delete file or folder", labels);
    }

    [Fact]
    public async Task Delete_file_or_folder_from_the_explorers_palette_still_takes_its_selection()
    {
        _fs.AddFile("/work/doomed.txt", new MockFileData("x"));
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => { workbench.Sidebar.Explorer.SelectedObject = Node(workbench, "doomed.txt"); },
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { foreach (var c in "delete") host.App.InjectKey(new Key(c)); },
            () => Palette(workbench)!.Labels.SequenceEqual(["Delete file or folder"]),
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is not null,
            () => host.App.InjectKey(Key.Tab),
            () => Confirm(workbench)!.FocusedChoice == "Delete",
            () => host.App.InjectKey(Key.Enter),
            () => Confirm(workbench) is null);

        Assert.False(_fs.File.Exists("/work/doomed.txt"));
        Assert.True(_fs.File.Exists("/work/a.txt"));
    }

    // Settings › Keyboard Shortcuts is the reference the narrowed palette sends you to, so it filters nothing.
    [Fact]
    public async Task Keyboard_Shortcuts_still_lists_a_command_the_editors_palette_leaves_out()
    {
        using var workbench = BuildWorkbench();
        var commands = new CommandService();
        var keybindings = new KeybindingService(commands);
        using var host = new WorkbenchHost(workbench, commands, keybindings, new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { labels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        var rows = KeybindingRows.Build(commands.Registered, [.. keybindings.Bindings], "");

        Assert.DoesNotContain("Delete file or folder", labels);
        Assert.Contains(rows, r => r.Label == "Delete file or folder" && r.Scope == CommandScope.Explorer);
        Assert.Empty(commands.Registered.Select(c => c.Id).Except(rows.Select(r => r.CommandId), StringComparer.Ordinal));
    }

    [Fact]
    public async Task What_needs_an_open_editor_is_only_offered_once_one_is()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> closed = [];
        IReadOnlyList<string> open = [];

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusSidebar),
            () => workbench.StatusBar.DisplayedFocus == "Explorer",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { closed = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { open = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.All(NeedAnEditor, label => Assert.DoesNotContain(label, closed));
        Assert.All(NeedAnEditor, label => Assert.Contains(label, open));
        Assert.Contains("Toggle sidebar", closed);
    }

    [Fact]
    public async Task Outside_a_git_repository_the_palette_leaves_the_git_commands_out()
    {
        _fs.AddFile("/scratch/b.txt", new MockFileData("one\n"));
        using var workbench = BuildWorkbench("/scratch");
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> labels = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/scratch/b.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { labels = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.DoesNotContain("Open pull request", labels);
        Assert.DoesNotContain("Compare to revision", labels);
        Assert.Contains("Compare to saved", labels);
    }

    // The review commands act on the pull request being reviewed, and creating a comment on the diff row
    // the keys are on — so the Review pane offers the first two and the diff all three.
    [Fact]
    public async Task The_review_commands_arrive_with_the_pull_request()
    {
        _git.Changes = [new GitChange(GitChangeKind.Modified, "a.txt")];
        _git.RepoFiles["b45e:a.txt"] = "one\nTWO\n";
        _gitHub.PullRequest = new GitHubPullRequest(284, "Scope the palette", "main", "feature", default);
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> fromReview = [];
        IReadOnlyList<string> fromDiff = [];

        await HostSteps.Run(host,
            () => commands.TryExecute(CommandIds.FocusReview),
            () => workbench.Sidebar.Review.Review?.PullRequest is not null,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { fromReview = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => workbench.Sidebar.Review.ListHasFocus,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null,
            () => commands.TryExecute(CommandIds.FocusEditorBody),
            () => workbench.StatusBar.DisplayedFocus == "Diff",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { fromDiff = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.Contains("PR overview", fromReview);
        Assert.Contains("Submit review", fromReview);
        Assert.DoesNotContain("Create comment", fromReview);
        Assert.Contains("Create comment", fromDiff);
    }

    // isEnabled already keeps a command's keys from firing where it can't act; the palette drops it too.
    [Fact]
    public async Task A_command_the_workbench_has_switched_off_isnt_offered()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out _);
        IReadOnlyList<string> alone = [];
        IReadOnlyList<string> withCursors = [];

        await HostSteps.Run(host,
            () => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt")),
            () => workbench.StatusBar.DisplayedFocus == "Editor",
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { alone = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null,
            () => host.App.InjectKey(Key.CursorDown.WithCtrl.WithAlt),
            () => workbench.Editor.Group.ActiveTab!.HasSecondaryCursors,
            () => host.App.InjectKey(Key.E.WithCtrl),
            () => Palette(workbench) is not null,
            () => { withCursors = Palette(workbench)!.Labels; host.App.InjectKey(Key.Esc); },
            () => Palette(workbench) is null);

        Assert.DoesNotContain("Remove secondary cursors", alone);
        Assert.Contains("Remove secondary cursors", withCursors);
    }

    private static ActionView? Palette(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ActionView>().SingleOrDefault();

    private static ConfirmView? Confirm(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<ConfirmView>().SingleOrDefault();

    private static IFileSystemInfo Node(Workbench.Workbench workbench, string name)
    {
        var explorer = workbench.Sidebar.Explorer;
        explorer.Expand(explorer.Root!);
        return explorer.GetChildren(explorer.Root!).Single(c => c.Name == name);
    }

    private Workbench.Workbench BuildWorkbench(string root = "/work")
    {
        var sidebar = new SidebarPart(new FileExplorerView(), review: new ReviewView(_git, _gitHub));
        var workbench = new Workbench.Workbench(sidebar, new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New(root));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git, gitHub: _gitHub);
    }
}
