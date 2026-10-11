using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Git;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

public class CommittedBaselinesTests
{
    private readonly WatchableFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly EditorGroup _group = new();
    private readonly CommittedBaselines _baselines;
    private readonly SemaphoreSlim _applied = new(0);
    private readonly List<Action> _flushes = [];

    public CommittedBaselinesTests()
    {
        Repo("/repo");
        _baselines = new CommittedBaselines(new EditorGroups(_group, new EditorGroup()), _git, action =>
        {
            action();
            _applied.Release();
        }, new HeadWatcher(_fs, (_, flush) => _flushes.Add(flush), NullLogger.Instance));
    }

    [Fact]
    public async Task A_tracked_file_is_marked_against_HEAD_as_soon_as_it_opens()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\ntwo\nthree\n";

        var tab = await Open("/repo/a.cs", "one\nTWO\nthree\n");

        Assert.Equal([None, Modified, None, None], tab.LineChanges);
    }

    [Fact]
    public async Task Saving_keeps_the_markers_until_the_lines_match_HEAD_again()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\ntwo\n";
        var tab = await Open("/repo/a.cs", "one\ntwo\n");

        tab.Replace(new TextMatch(1, 0, 3), "TWO");
        tab.Save();
        await _baselines.Idle;
        var saved = tab.LineChanges.ToArray();
        tab.Replace(new TextMatch(1, 0, 3), "two");

        Assert.Equal([None, Modified, None], saved);
        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("error")]
    public async Task Without_a_HEAD_version_the_markers_show_changes_since_the_save(string why)
    {
        if (why == "error") _git.HeadFileError = "git isn't installed or isn't on PATH";
        var tab = await Open("/repo/a.cs", "one\ntwo\n");

        tab.Replace(new TextMatch(1, 0, 3), "TWO");
        var edited = tab.LineChanges.ToArray();
        tab.Save();
        await _baselines.Idle;

        Assert.Equal([None, Modified, None], edited);
        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public async Task A_file_outside_a_repo_never_asks_git()
    {
        var tab = await Open("/elsewhere/a.cs", "one\n");

        tab.Save();
        await _baselines.Idle;

        Assert.Empty(_git.HeadReads);
    }

    [Fact]
    public async Task Reloading_reads_HEAD_again()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\n";
        var tab = await Open("/repo/a.cs", "one\n");

        _git.HeadFiles[Full("/repo/a.cs")] = "uno\n";
        _fs.File.WriteAllText(Full("/repo/a.cs"), "uno\ndos\n");
        tab.Reload();
        await _baselines.Idle;

        Assert.Equal([None, Added, None], tab.LineChanges);
    }

    [Fact]
    public async Task A_read_that_returns_late_never_replaces_a_newer_one()
    {
        var tab = await Open("/repo/a.cs", "new\n");
        _applied.Wait(0);
        var reads = new ConcurrentQueue<TaskCompletionSource<string?>>();
        using var started = new SemaphoreSlim(0);
        _git.HeadFileSource = _ =>
        {
            var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            reads.Enqueue(read);
            started.Release();
            return read.Task;
        };
        var ct = TestContext.Current.CancellationToken;

        tab.Save();
        await started.WaitAsync(ct);
        reads.TryDequeue(out var first);
        tab.Save();
        await started.WaitAsync(ct);
        reads.TryDequeue(out var second);
        second!.SetResult("new\n");
        await _applied.WaitAsync(ct);
        first!.SetResult("old\n");
        await _baselines.Idle;

        Assert.Equal(["new", ""], tab.CommittedLines);
    }

    [Fact]
    public async Task A_read_that_returns_after_its_tab_closed_is_dropped()
    {
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _git.HeadFileSource = _ => read.Task;
        var tab = _group.OpenOrFocus(Write("/repo/a.cs", "new\n"));

        _group.CloseAll();
        read.SetResult("old\n");
        await _baselines.Idle;

        Assert.Null(tab.CommittedLines);
    }

    [Fact]
    public async Task A_CRLF_checkout_matches_its_HEAD_line_for_line()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\r\ntwo\r\n";

        var tab = await Open("/repo/a.cs", "one\r\ntwo\r\n");

        Assert.NotNull(tab.CommittedLines);
        Assert.All(tab.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public async Task A_commit_elsewhere_clears_the_markers_on_every_tab_from_that_repo()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\n";
        _git.HeadFiles[Full("/repo/b.cs")] = "uno\n";
        var a = await Open("/repo/a.cs", "ONE\n");
        var b = await Open("/repo/b.cs", "UNO\n");

        _git.HeadFiles[Full("/repo/a.cs")] = "ONE\n";
        _git.HeadFiles[Full("/repo/b.cs")] = "UNO\n";
        await Commit("/repo/.git", "refs/heads/main");

        Assert.All(a.LineChanges, c => Assert.Equal(None, c));
        Assert.All(b.LineChanges, c => Assert.Equal(None, c));
    }

    [Fact]
    public async Task A_partial_commit_leaves_only_the_uncommitted_lines_marked()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "one\ntwo\n";
        var tab = await Open("/repo/a.cs", "ONE\nTWO\n");

        _git.HeadFiles[Full("/repo/a.cs")] = "ONE\ntwo\n";
        await Commit("/repo/.git", "refs/heads/main");

        Assert.Equal([None, Modified, None], tab.LineChanges);
    }

    [Fact]
    public async Task Undoing_a_commit_brings_its_markers_back()
    {
        _git.HeadFiles[Full("/repo/a.cs")] = "ONE\n";
        var tab = await Open("/repo/a.cs", "ONE\n");

        _git.HeadFiles[Full("/repo/a.cs")] = "one\n";
        await Commit("/repo/.git", "refs/heads/main");

        Assert.Equal([Modified, None], tab.LineChanges);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("packed-refs")]
    public async Task Switching_branch_or_packing_refs_reads_HEAD_again(string file)
    {
        await Open("/repo/a.cs", "one\n");

        await Commit("/repo/.git", file, watched: "/repo/.git");

        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Fact]
    public async Task A_branch_with_a_slash_in_its_name_is_followed_into_its_folder()
    {
        _fs.File.WriteAllText(Full("/repo/.git/HEAD"), "ref: refs/heads/feat/x\n");
        await Open("/repo/a.cs", "one\n");

        await Commit("/repo/.git", "refs/heads/feat/x");

        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Theory]
    [InlineData("refs/heads/other")]
    [InlineData("index")]
    [InlineData("refs/heads/main.lock")]
    public async Task Nothing_but_HEAD_and_its_branch_reads_HEAD_again(string file)
    {
        await Open("/repo/a.cs", "one\n");

        await Commit("/repo/.git", file, watched: file == "index" ? "/repo/.git" : "/repo/.git/refs/heads");

        Assert.Single(_git.HeadReads);
    }

    [Fact]
    public async Task Tabs_from_another_repo_and_outside_any_repo_are_left_alone()
    {
        Repo("/other");
        await Open("/repo/a.cs", "one\n");
        await Open("/other/b.cs", "one\n");
        await Open("/elsewhere/c.cs", "one\n");

        await Commit("/repo/.git", "refs/heads/main");

        Assert.Equal([Full("/repo/a.cs"), Full("/other/b.cs"), Full("/repo/a.cs")], _git.HeadReads);
    }

    [Fact]
    public async Task A_rebase_reads_HEAD_once_when_it_settles()
    {
        await Open("/repo/a.cs", "one\n");
        var branches = _fs.Watchers.For(Full("/repo/.git/refs/heads"));

        for (var i = 0; i < 5; i++) branches.Raise(WatcherChangeTypes.Renamed, Full("/repo/.git/refs/heads/main"));
        _fs.Watchers.For(Full("/repo/.git")).Raise(WatcherChangeTypes.Renamed, Full("/repo/.git/HEAD"));
        await Flush();

        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Fact]
    public async Task A_linked_worktree_follows_its_own_HEAD_and_the_shared_branches()
    {
        _fs.AddFile(Full("/repo/.git/worktrees/wt/HEAD"), new MockFileData("ref: refs/heads/wt\n"));
        _fs.AddFile(Full("/repo/.git/worktrees/wt/commondir"), new MockFileData("../..\n"));
        _fs.AddFile(Full("/wt/.git"), new MockFileData($"gitdir: {Full("/repo/.git/worktrees/wt")}\n"));
        await Open("/wt/a.cs", "one\n");

        await Commit("/repo/.git", "refs/heads/wt");
        await Commit("/repo/.git/worktrees/wt", "HEAD", watched: "/repo/.git/worktrees/wt");
        await Commit("/repo/.git", "refs/heads/main");

        Assert.Equal(3, _git.HeadReads.Count);
    }

    [Fact]
    public async Task A_detached_HEAD_moves_with_HEAD_alone()
    {
        _fs.File.WriteAllText(Full("/repo/.git/HEAD"), "4a91c0e2f00d\n");
        await Open("/repo/a.cs", "one\n");

        await Commit("/repo/.git", "refs/heads/main");
        await Commit("/repo/.git", "HEAD", watched: "/repo/.git");

        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Fact]
    public async Task A_repo_that_cant_be_watched_catches_up_when_its_tab_is_shown()
    {
        _fs.Watchers.FailFor.Add(Full("/repo/.git/refs/heads"));
        var a = await Open("/repo/a.cs", "ONE\n");
        await Open("/elsewhere/b.cs", "one\n");
        var reads = _git.HeadReads.Count;

        _git.HeadFiles[Full("/repo/a.cs")] = "one\n";
        _group.Focus(a.File.FullName);
        await _baselines.Idle;

        Assert.Equal(reads + 1, _git.HeadReads.Count);
        Assert.Equal([Modified, None], a.LineChanges);
    }

    [Fact]
    public async Task A_watched_repo_reads_nothing_when_its_tab_is_shown()
    {
        var a = await Open("/repo/a.cs", "one\n");
        await Open("/repo/b.cs", "one\n");

        _group.Focus(a.File.FullName);
        await _baselines.Idle;

        Assert.Equal(2, _git.HeadReads.Count);
    }

    [Fact]
    public async Task The_watch_ends_when_the_last_tab_from_its_repo_closes()
    {
        var a = await Open("/repo/a.cs", "one\n");
        await Open("/repo/b.cs", "one\n");

        _group.CloseUnder(a.File.FullName);
        var whileOneOpen = _fs.Watchers.Live.Count();
        _group.CloseAll();

        Assert.Equal(2, whileOneOpen);
        Assert.Empty(_fs.Watchers.Live);
    }

    private async Task<EditorTab> Open(string path, string content)
    {
        var tab = _group.OpenOrFocus(Write(path, content));
        await _baselines.Idle;
        return tab;
    }

    private IFileInfo Write(string path, string content)
    {
        _fs.AddFile(Full(path), new MockFileData(content));
        return _fs.FileInfo.New(Full(path));
    }

    private string Full(string path) => _fs.Path.GetFullPath(path);

    private void Repo(string root)
    {
        _fs.AddFile(Full($"{root}/.git/HEAD"), new MockFileData("ref: refs/heads/main\n"));
        _fs.AddDirectory(Full($"{root}/.git/refs/heads"));
    }

    /// <summary>Git writes <paramref name="file"/> under <paramref name="gitDir"/> the way it does: to a lock, renamed into place.</summary>
    private async Task Commit(string gitDir, string file, string? watched = null)
    {
        var path = Full($"{gitDir}/{file}");
        _fs.Watchers.For(Full(watched ?? $"{gitDir}/refs/heads")).Raise(WatcherChangeTypes.Renamed, path);
        await Flush();
    }

    private async Task Flush()
    {
        foreach (var flush in _flushes.ToList()) flush();
        _flushes.Clear();
        await _baselines.Idle;
    }
}
