using System.Collections.Concurrent;
using TuiCode.Abstractions;
using TuiCode.Editor;
using TuiCode.Workbench.Git;
using static TuiCode.Editor.LineChange;

namespace TuiCode.Tests;

public class CommittedBaselinesTests
{
    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly EditorGroup _group = new();
    private readonly CommittedBaselines _baselines;
    private readonly SemaphoreSlim _applied = new(0);

    public CommittedBaselinesTests()
    {
        _fs.AddDirectory(Full("/repo/.git"));
        _baselines = new CommittedBaselines(_group, _git, action =>
        {
            action();
            _applied.Release();
        });
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
}
