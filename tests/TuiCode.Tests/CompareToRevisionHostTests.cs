using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Explorer;
using TuiCode.Workbench;
using TuiCode.Workbench.Git;
using TuiCode.Workbench.Parts;
using TuiCode.Workbench.Services;

namespace TuiCode.Tests;

// Drives `ctr` through the host against a fake git. Boots a TG Application — serialised (#77).
public class CompareToRevisionHostTests : StaticConfigurationTest
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();

    public CompareToRevisionHostTests()
    {
        _fs.AddFile("/work/a.txt", new MockFileData("alpha\nbravo\n"));
        _git.Root = "/work";
        _git.Refs = [new GitRef("main", GitRefKind.Branch), new GitRef("origin/main", GitRefKind.RemoteBranch), new GitRef("v1.0", GitRefKind.Tag)];
        _git.Commits = [new GitCommit("3f2a9c1", "Add bravo", DateTimeOffset.UnixEpoch)];
        _git.Files["HEAD"] = "alpha\n";
        _git.Files["main"] = "alpha\n";
        _git.Files["3f2a9c1"] = "alpha\nBRAVO\n";
        _git.Resolvable.UnionWith(["HEAD", "main", "origin/main", "v1.0", "3f2a9c1", "HEAD~3"]);
    }

    [Fact]
    public async Task Ctr_lists_HEAD_branches_tags_and_commits_in_order()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        RevisionPickerView? view = null;
        IReadOnlyList<string> rows = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => host.App.InjectKey(Key.Space.WithCtrl),
            () => host.App.InjectKey(Key.C),
            () => host.App.InjectKey(Key.T),
            () => host.App.InjectKey(Key.R),
            () => (view = Picker(workbench)) is { Loaded: true },
            () => { rows = view!.VisibleItems; },
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("Compare a.txt to revision", view!.Title);
        Assert.Equal(["HEAD", "main", "origin/main", "v1.0", "3f2a9c1  Add bravo"], rows);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task Ctr_then_Enter_compares_against_HEAD()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        int? selected = null;

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is not null,
            () => { selected = Picker(workbench)!.SelectedItem; },
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.Equal(0, selected);
        Assert.Equal("a.txt ↔ HEAD", Assert.Single(workbench.Editor.Group.DiffTabs).Title);
    }

    [Fact]
    public async Task Typing_HEAD_picks_HEAD_over_a_branch_it_also_matches()
    {
        _git.Refs = [new GitRef("feature/header", GitRefKind.Branch), .. _git.Refs];
        _git.Files["feature/header"] = "zulu\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => Type(host, "HEAD"),
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["HEAD", "feature/header"]),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.Equal("a.txt ↔ HEAD", Assert.Single(workbench.Editor.Group.DiffTabs).Title);
    }

    [Fact]
    public async Task A_typed_ref_that_isnt_listed_opens_a_diff_tab_titled_as_typed()
    {
        _git.Files["HEAD~3"] = "zulu\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => Type(host, "HEAD~3"),
            () => Picker(workbench)!.VisibleItems.Count == 0,
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        var diff = Assert.Single(workbench.Editor.Group.DiffTabs);
        Assert.Equal("a.txt ↔ HEAD~3", diff.Title);
        Assert.Equal(DiffRowKind.Modified, diff.Diff.Rows[0].Kind);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task Picking_a_commit_shows_it_on_the_left_titled_with_its_short_hash()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => Type(host, "bravo"),
            () => Picker(workbench)!.VisibleItems.SequenceEqual(["3f2a9c1  Add bravo"]),
            () => host.App.InjectKey(Key.Enter),
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        var diff = Assert.Single(workbench.Editor.Group.DiffTabs);
        Assert.Equal("a.txt ↔ 3f2a9c1", diff.Title);
        Assert.Equal([DiffRowKind.Both, DiffRowKind.Modified], diff.Diff.Rows.Select(r => r.Kind).Take(2));
    }

    [Fact]
    public async Task Down_moves_the_selection_and_Enter_picks_that_row()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorDown),
            () => host.App.InjectKey(Key.CursorUp),
            () => Picker(workbench)!.SelectedItem == 2,
            () =>
            {
                _git.Files["origin/main"] = "alpha\nzulu\n";
                host.App.InjectKey(Key.Enter);
            },
            () => workbench.Editor.Group.ActiveDiffTab is not null);

        Assert.Equal("a.txt ↔ origin/main", Assert.Single(workbench.Editor.Group.DiffTabs).Title);
    }

    [Fact]
    public async Task PageDown_and_PageUp_move_the_selection_a_page_at_a_time_and_stop_at_the_ends()
    {
        _git.Commits = Enumerable.Range(1, 40).Select(i => new GitCommit($"{i:x7}", $"Commit {i}", DateTimeOffset.UnixEpoch)).ToList();
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var selected = new List<int?>();
        void Record() => selected.Add(Picker(workbench)!.SelectedItem);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.PageDown),
            Record,
            () => host.App.InjectKey(Key.PageDown),
            () => host.App.InjectKey(Key.PageDown),
            Record,
            () => host.App.InjectKey(Key.PageUp),
            Record);

        Assert.Equal([16, 43, 27], selected);
    }

    [Fact]
    public async Task An_unknown_ref_shows_an_error_and_keeps_the_picker_open_with_the_text()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var error = "";
        var filter = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => Type(host, "mian~"),
            () => host.App.InjectKey(Key.Enter),
            () => (error = Picker(workbench)!.Error).Length > 0,
            () => { filter = Picker(workbench)!.Filter; },
            () => host.App.InjectKey(Key.Esc),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("No branch, tag or commit called 'mian~'", error);
        Assert.Equal("mian~", filter);
        Assert.Empty(workbench.Editor.Group.DiffTabs);
        Assert.Null(Picker(workbench));
    }

    [Fact]
    public async Task A_file_missing_from_the_revision_is_an_error_in_the_picker()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var error = "";

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => Type(host, "v1"),
            () => host.App.InjectKey(Key.Enter),
            () => (error = Picker(workbench)!.Error).Length > 0,
            () => host.App.InjectKey(Key.Esc),
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal("a.txt isn't in v1.0", error);
        Assert.Empty(workbench.Editor.Group.DiffTabs);
    }

    [Fact]
    public async Task Running_ctr_twice_with_the_same_ref_focuses_the_one_diff_tab_without_reading_it_again()
    {
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        var group = workbench.Editor.Group;

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is not null,
            () => { group.OpenOrFocus(group.Tabs[0].File); },
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.Enter),
            () => group.ActiveDiffTab is not null);

        Assert.Equal("a.txt ↔ HEAD", Assert.Single(group.DiffTabs).Title);
        Assert.Equal(1, _git.ShowCount);
    }

    [Fact]
    public async Task A_buffer_identical_to_the_revision_says_so_and_opens_no_tab()
    {
        _git.Files["HEAD"] = "alpha\nbravo\n";
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is { Loaded: true },
            () => host.App.InjectKey(Key.Enter),
            () => Picker(workbench) is null);

        Assert.Empty(workbench.Editor.Group.DiffTabs);
        Assert.Equal("No changes against HEAD", workbench.StatusBar.DisplayedText);
    }

    [Fact]
    public async Task The_picker_opens_before_the_list_loads_and_fills_in_after()
    {
        var gate = new TaskCompletionSource();
        _git.RefsGate = gate.Task;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);
        IReadOnlyList<string> rowsBeforeLoad = [];

        await HostSteps.Run(host,
            () => OpenFile(workbench),
            () => commands.TryExecute(CommandIds.CompareToRevision),
            () => Picker(workbench) is not null,
            () =>
            {
                rowsBeforeLoad = Picker(workbench)!.VisibleItems;
                gate.SetResult();
            },
            () => Picker(workbench)!.VisibleItems.Count == 5,
            () => host.App.InjectKey(Key.Esc));

        Assert.Equal(["HEAD"], rowsBeforeLoad);
    }

    [Theory]
    [InlineData("none", "No file is open.")]
    [InlineData("untitled", "a.txt has never been saved.")]
    [InlineData("no repo", "a.txt isn't in a git repository.")]
    [InlineData("no git", "git isn't installed or isn't on PATH")]
    public async Task Ctr_with_nothing_to_compare_says_why_and_opens_no_picker(string state, string message)
    {
        if (state == "no repo") _git.Root = null;
        if (state == "no git") _git.Missing = true;
        using var workbench = BuildWorkbench();
        using var host = BuildHost(workbench, out var commands);

        await HostSteps.Run(host,
            () =>
            {
                if (state != "none") OpenFile(workbench);
                if (state == "untitled") _fs.File.Delete("/work/a.txt");
                commands.TryExecute(CommandIds.CompareToRevision);
            },
            () => workbench.StatusBar.DisplayedText == message);

        Assert.Null(Picker(workbench));
        Assert.Empty(workbench.Editor.Group.DiffTabs);
    }

    private static RevisionPickerView? Picker(Workbench.Workbench workbench) =>
        workbench.SubViews.OfType<RevisionPickerView>().SingleOrDefault();

    private static void Type(WorkbenchHost host, string text)
    {
        foreach (var c in text) host.App.InjectKey(new Key(c));
    }

    private void OpenFile(Workbench.Workbench workbench) => workbench.OpenFile(_fs.FileInfo.New("/work/a.txt"));

    private Workbench.Workbench BuildWorkbench()
    {
        var workbench = new Workbench.Workbench(new SidebarPart(new FileExplorerView()), new EditorPart(), new StatusBarPart());
        workbench.Sidebar.Explorer.Open(_fs.DirectoryInfo.New("/work"));
        return workbench;
    }

    private WorkbenchHost BuildHost(Workbench.Workbench workbench, out CommandService commands)
    {
        commands = new CommandService();
        return new WorkbenchHost(workbench, commands, new KeybindingService(commands), new InputScopeStack(),
            new InMemorySettingsService(), driverName: DriverRegistry.Names.ANSI, git: _git);
    }

    private sealed class FakeGitCli : IGitCli
    {
        private const string NoGit = "git isn't installed or isn't on PATH";

        public string? Root { get; set; }
        public bool Missing { get; set; }
        public IReadOnlyList<GitRef> Refs { get; set; } = [];
        public IReadOnlyList<GitCommit> Commits { get; set; } = [];
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Resolvable { get; } = new(StringComparer.Ordinal);
        public Task RefsGate { get; set; } = Task.CompletedTask;
        public int ShowCount { get; private set; }

        public Task<GitResult<string?>> GetRepoRootAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Missing ? GitResult<string?>.Failure(NoGit) : GitResult<string?>.Success(Root));

        public Task<GitResult<string?>> ShowFileAsync(string filePath, string revision, CancellationToken cancellationToken = default)
        {
            ShowCount++;
            return Task.FromResult(GitResult<string?>.Success(Files.GetValueOrDefault(revision)));
        }

        public async Task<GitResult<IReadOnlyList<GitRef>>> GetRefsAsync(string path, CancellationToken cancellationToken = default)
        {
            await RefsGate;
            return GitResult<IReadOnlyList<GitRef>>.Success(Refs);
        }

        public Task<GitResult<IReadOnlyList<GitCommit>>> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(GitResult<IReadOnlyList<GitCommit>>.Success(Commits));

        public Task<GitResult<bool>> ResolvesAsync(string path, string revision, CancellationToken cancellationToken = default) =>
            Task.FromResult(GitResult<bool>.Success(Resolvable.Contains(revision)));
    }
}
