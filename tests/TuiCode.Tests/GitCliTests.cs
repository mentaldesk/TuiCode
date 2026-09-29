using System.Diagnostics;
using System.Runtime.Versioning;
using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class GitCliTests
{
    [Fact]
    public void ParseRefs_reads_each_kind_and_orders_branches_then_remotes_then_tags()
    {
        var output =
            "refs/tags/v1.0\0v1.0\0\n" +
            "refs/heads/feature/new thing\0feature/new thing\0\n" +
            "refs/remotes/origin/HEAD\0origin/HEAD\0refs/remotes/origin/main\n" +
            "refs/remotes/origin/main\0origin/main\0\n" +
            "refs/heads/main\0main\0\n";

        var refs = GitCli.ParseRefs(output);

        Assert.Equal(
        [
            new GitRef("feature/new thing", GitRefKind.Branch),
            new GitRef("main", GitRefKind.Branch),
            new GitRef("origin/main", GitRefKind.RemoteBranch),
            new GitRef("v1.0", GitRefKind.Tag),
        ], refs);
    }

    [Fact]
    public void ParseLog_keeps_subjects_with_spaces_tabs_and_emoji()
    {
        var output =
            "3f2a9c1\02026-09-18T10:15:00+12:00\0Register IGitCli\tin Program.cs 🚀\n" +
            "a1b2c3d\02026-09-17T08:00:00Z\0Subject with \0 a stray NUL\n" +
            "garbage line\n";

        var commits = GitCli.ParseLog(output);

        Assert.Equal(
        [
            new GitCommit("3f2a9c1", "Register IGitCli\tin Program.cs 🚀", new DateTimeOffset(2026, 9, 18, 10, 15, 0, TimeSpan.FromHours(12))),
            new GitCommit("a1b2c3d", "Subject with \0 a stray NUL", new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero)),
        ], commits);
    }

    [Fact]
    public async Task Every_query_fails_when_git_is_missing()
    {
        var git = new GitCli(new MockFileSystem(), executable: "tuicode-no-such-git");
        var ct = TestContext.Current.CancellationToken;

        Assert.False((await git.GetRepoRootAsync("/repo/a.cs", ct)).Succeeded);
        Assert.False((await git.ShowFileAsync("/repo/a.cs", "main", ct)).Succeeded);
        Assert.False((await git.GetRefsAsync("/repo/a.cs", ct)).Succeeded);
        Assert.False((await git.GetFileHistoryAsync("/repo/a.cs", ct)).Succeeded);
        Assert.False((await git.ResolvesAsync("/repo/a.cs", "main", ct)).Succeeded);
        Assert.False((await git.GetCurrentBranchAsync("/repo", ct)).Succeeded);
        Assert.False((await git.GetDefaultBranchAsync("/repo", ct)).Succeeded);
        Assert.False((await git.GetMergeBaseAsync("/repo", "HEAD", "main", ct)).Succeeded);
        Assert.False((await git.GetChangedFilesAsync("/repo", "main", ct)).Succeeded);
        Assert.False((await git.ShowRepoFileAsync("/repo", "a.cs", "main", ct)).Succeeded);
        Assert.False((await git.AddWorktreeAsync("/repo", "/repo/../pr-1", ct)).Succeeded);
        Assert.False((await git.FindWorktreeAsync("/repo", "main", ct)).Succeeded);
        Assert.False((await git.BlameAsync("/repo/a.cs", 1, cancellationToken: ct)).Succeeded);
    }

    [Fact]
    public void ParseChanges_reads_statuses_and_both_paths_of_a_rename()
    {
        var output = "M\0src/a.cs\0R087\0old dir/b.cs\0new dir/b.cs\0A\0c.cs\0D\0d.cs\0C100\0e.cs\0f.cs\0T\0g\0";

        var changes = GitCli.ParseChanges(output);

        Assert.Equal(
        [
            new GitChange(GitChangeKind.Modified, "src/a.cs"),
            new GitChange(GitChangeKind.Renamed, "new dir/b.cs", "old dir/b.cs"),
            new GitChange(GitChangeKind.Added, "c.cs"),
            new GitChange(GitChangeKind.Deleted, "d.cs"),
            new GitChange(GitChangeKind.Added, "f.cs"),
            new GitChange(GitChangeKind.Modified, "g"),
        ], changes);
    }

    [Fact]
    public void ParseWorktrees_reads_the_path_and_branch_of_each_worktree_in_the_porcelain_listing()
    {
        var output = "worktree /code/TuiCode/main\nHEAD abc\nbranch refs/heads/main\n\nworktree /code/pr-184\nHEAD def\ndetached\n";

        Assert.Equal(
        [
            new GitCli.Worktree("/code/TuiCode/main", "main"),
            new GitCli.Worktree("/code/pr-184", null),
        ], GitCli.ParseWorktrees(output));
    }

    [Fact]
    public async Task A_branch_already_checked_out_is_found_by_the_worktree_it_is_in()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "one\n", "First");
        repo.Git("worktree", "add", "-q", "-b", "feature", repo.File("feature"));
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var found = await git.FindWorktreeAsync(repo.Path, "feature", ct);
        var missing = await git.FindWorktreeAsync(repo.Path, "no-such-branch", ct);

        Assert.Equal("feature", Path.GetFileName(found.Value));
        Assert.True(File.Exists(Path.Combine(found.Value!, "a.cs")));
        Assert.True(missing.Succeeded);
        Assert.Null(missing.Value);
    }

    [Fact]
    public async Task A_worktree_is_created_once_and_reused_after_that()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "one\n", "First");
        var worktree = repo.File("pr-184");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var created = await git.AddWorktreeAsync(repo.Path, worktree, ct);
        var reused = await git.AddWorktreeAsync(repo.Path, worktree, ct);

        Assert.True(created.Value);
        Assert.False(reused.Value);
        Assert.True(reused.Succeeded);
        Assert.True(File.Exists(Path.Combine(worktree, "a.cs")));
    }

    [Fact]
    public async Task A_worktree_over_a_path_that_is_in_the_way_fails_with_git_s_own_message()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "one\n", "First");
        repo.Write("pr-184/stray.txt", "in the way\n");
        var git = new GitCli(new FileSystem());

        var result = await git.AddWorktreeAsync(repo.Path, repo.File("pr-184"), TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("pr-184", result.Error);
    }

    [Fact]
    public async Task Queries_outside_a_repo_find_no_root_and_fail()
    {
        using var dir = new TempRepo(init: false);
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var root = await git.GetRepoRootAsync(dir.Path, ct);
        var show = await git.ShowFileAsync(dir.File("a.cs"), "HEAD", ct);

        Assert.True(root.Succeeded);
        Assert.Null(root.Value);
        Assert.False(show.Succeeded);
        Assert.Contains("not a git repository", show.Error);
    }

    [Fact]
    public async Task Reads_files_refs_and_history_from_a_real_repo()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("src/a.cs", "one\n", "First commit 🎉");
        repo.Git("tag", "v1");
        repo.Commit("src/a.cs", "two\n", "Second commit");
        repo.Commit("src/b.cs", "new\n", "Add b");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var root = await git.GetRepoRootAsync(repo.File("src/a.cs"), ct);
        Assert.Equal(Path.GetFileName(repo.Path), Path.GetFileName(root.Value));

        Assert.Equal("one\n", (await git.ShowFileAsync(repo.File("src/a.cs"), "v1", ct)).Value);
        Assert.Equal("two\n", (await git.ShowFileAsync(repo.File("src/a.cs"), "HEAD", ct)).Value);
        var missing = await git.ShowFileAsync(repo.File("src/b.cs"), "v1", ct);
        Assert.True(missing.Succeeded);
        Assert.Null(missing.Value);
        Assert.False((await git.ShowFileAsync(repo.File("src/a.cs"), "no-such-branch", ct)).Succeeded);

        var refs = (await git.GetRefsAsync(repo.File("src/a.cs"), ct)).Value;
        Assert.Equal([new GitRef("main", GitRefKind.Branch), new GitRef("v1", GitRefKind.Tag)], refs);

        var history = (await git.GetFileHistoryAsync(repo.File("src/a.cs"), ct)).Value;
        Assert.Equal(["Second commit", "First commit 🎉"], history.Select(c => c.Subject));

        Assert.True((await git.ResolvesAsync(repo.Path, "HEAD~2", ct)).Value);
        Assert.False((await git.ResolvesAsync(repo.Path, "HEAD~3", ct)).Value);
        Assert.False((await git.ResolvesAsync(repo.Path, "--all", ct)).Value);
    }

    [Fact]
    public async Task Lists_a_branch_changes_against_its_merge_base_with_the_default_branch()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("keep.cs", "same\n", "Base");
        repo.Commit("src/mod.cs", "one\n", "Add mod");
        repo.Commit("src/gone.cs", "bye\n", "Add gone");
        repo.Commit("old/moved.cs", "a file long enough for git to spot the rename\n", "Add moved");
        repo.Git("checkout", "-q", "-b", "feature");
        repo.Commit("src/mod.cs", "two\n", "Change mod");
        repo.Commit("src/new.cs", "new\n", "Add new");
        repo.Git("rm", "-q", "src/gone.cs");
        repo.Git("mv", "old/moved.cs", "moved.cs");
        repo.Write("staged.cs", "staged\n");
        repo.Git("add", "staged.cs");
        repo.Write("keep.cs", "edited, not staged\n");
        repo.Write("untracked.cs", "not added\n");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("feature", (await git.GetCurrentBranchAsync(repo.Path, ct)).Value);
        Assert.Equal("main", (await git.GetDefaultBranchAsync(repo.Path, ct)).Value);
        var mergeBase = (await git.GetMergeBaseAsync(repo.Path, "HEAD", "main", ct)).Value!;
        var changes = (await git.GetChangedFilesAsync(repo.Path, mergeBase, ct)).Value;

        Assert.Equal(
        [
            new GitChange(GitChangeKind.Modified, "keep.cs"),
            new GitChange(GitChangeKind.Renamed, "moved.cs", "old/moved.cs"),
            new GitChange(GitChangeKind.Deleted, "src/gone.cs"),
            new GitChange(GitChangeKind.Modified, "src/mod.cs"),
            new GitChange(GitChangeKind.Added, "src/new.cs"),
            new GitChange(GitChangeKind.Added, "staged.cs"),
        ], changes.OrderBy(c => c.Path, StringComparer.Ordinal));
        Assert.Equal("a file long enough for git to spot the rename\n", (await git.ShowRepoFileAsync(repo.Path, "old/moved.cs", mergeBase, ct)).Value);
        Assert.Null((await git.ShowRepoFileAsync(repo.Path, "src/new.cs", mergeBase, ct)).Value);
    }

    [Fact]
    public async Task The_default_branch_is_origin_HEAD_then_main_then_master()
    {
        using var repo = new TempRepo(init: true);
        repo.Git("checkout", "-q", "-b", "master");
        repo.Commit("a.cs", "a\n", "First");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal("master", (await git.GetDefaultBranchAsync(repo.Path, ct)).Value);
        repo.Git("branch", "main");
        Assert.Equal("main", (await git.GetDefaultBranchAsync(repo.Path, ct)).Value);
        repo.Git("update-ref", "refs/remotes/origin/trunk", "HEAD");
        repo.Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/trunk");
        Assert.Equal("origin/trunk", (await git.GetDefaultBranchAsync(repo.Path, ct)).Value);

        repo.Git("checkout", "-q", "--detach");
        Assert.Null((await git.GetCurrentBranchAsync(repo.Path, ct)).Value);
    }

    [Fact]
    public async Task Unrelated_histories_have_no_merge_base()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "a\n", "First");
        repo.Git("checkout", "-q", "--orphan", "other");
        repo.Commit("b.cs", "b\n", "Unrelated");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var mergeBase = await git.GetMergeBaseAsync(repo.Path, "HEAD", "main", ct);

        Assert.True(mergeBase.Succeeded);
        Assert.Null(mergeBase.Value);
    }

    [Fact]
    public void ParseBlame_reads_the_author_date_subject_and_line_of_a_commit()
    {
        var output =
            "93f4047d5fbc2a7433901c533b3ef70496e39db8 2 2 1\n" +
            "author Zoë \"Z\" O'Brien\n" +
            "author-mail <z@example.com>\n" +
            "author-time 1788480000\n" +
            "author-tz -0530\n" +
            "committer Someone Else\n" +
            "committer-time 1788490000\n" +
            "committer-tz +0000\n" +
            "summary Add a — the first 🎉 (#12)\n" +
            "boundary\n" +
            "filename src/a b.cs\n" +
            "\t    if (x)\tthen;\n";

        var line = GitCli.ParseBlame(output);

        Assert.Equal(new GitBlameLine(
            "93f4047d5fbc2a7433901c533b3ef70496e39db8", "Zoë \"Z\" O'Brien",
            DateTimeOffset.FromUnixTimeSeconds(1788480000).ToOffset(new TimeSpan(-5, -30, 0)),
            "Add a — the first 🎉 (#12)", "    if (x)\tthen;", "src/a b.cs"), line);
        Assert.True(line!.IsCommitted);
        Assert.Equal("93f4047", line.ShortHash);
        Assert.Equal(TimeSpan.FromMinutes(-330), line.Date.Offset);
    }

    [Fact]
    public void ParseBlame_reads_the_parent_and_the_path_there()
    {
        var output =
            "93f4047d5fbc2a7433901c533b3ef70496e39db8 2 2 1\n" +
            "summary Move it\n" +
            "previous 1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e old dir/a b.cs\n" +
            "filename new/a.cs\n" +
            "\tline\n";

        var line = GitCli.ParseBlame(output)!;

        Assert.Equal("new/a.cs", line.Path);
        Assert.Equal(new GitBlamePrevious("1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e", "old dir/a b.cs"), line.Previous);
    }

    [Fact]
    public void ParseBlame_marks_a_zero_hash_line_as_not_committed()
    {
        var output =
            "0000000000000000000000000000000000000000 1 1 1\r\n" +
            "author External file (--contents)\r\n" +
            "author-time 1790660840\r\n" +
            "author-tz +1300\r\n" +
            "summary Version of a.txt from standard input\r\n" +
            "\tzero\r\n";

        var line = GitCli.ParseBlame(output);

        Assert.False(line!.IsCommitted);
        Assert.Equal("zero", line.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fatal: no such path\n")]
    [InlineData("93f4047d5fbc2a7433901c533b3ef70496e39db8 2 2 1\nauthor A\n")]
    public void ParseBlame_gives_nothing_for_output_without_a_line(string output) =>
        Assert.Null(GitCli.ParseBlame(output));

    [Fact]
    public async Task Blames_a_saved_line_and_an_unsaved_buffer_as_it_is_on_screen()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("src/a.cs", "one\ntwo\n", "First");
        repo.Commit("src/a.cs", "one\nTWO\n", "Shout two 🎉");
        repo.Write("untracked.cs", "new\n");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var saved = (await git.BlameAsync(repo.File("src/a.cs"), 2, cancellationToken: ct)).Value;
        var shifted = (await git.BlameAsync(repo.File("src/a.cs"), 4, "added\nabove\none\nTWO\n", ct)).Value;
        var added = (await git.BlameAsync(repo.File("src/a.cs"), 1, "added\nabove\none\nTWO\n", ct)).Value;
        var untracked = await git.BlameAsync(repo.File("untracked.cs"), 1, cancellationToken: ct);
        var pastTheEnd = await git.BlameAsync(repo.File("src/a.cs"), 9, cancellationToken: ct);

        Assert.Equal(("TWO", "Shout two 🎉", "Test"), (saved!.Text, saved.Subject, saved.Author));
        Assert.Equal(saved.Hash, shifted!.Hash);
        Assert.Equal("TWO", shifted.Text);
        Assert.False(added!.IsCommitted);
        Assert.Equal("added", added.Text);
        Assert.True(untracked.Succeeded);
        Assert.Null(untracked.Value);
        Assert.False(pastTheEnd.Succeeded);
    }

    [Fact]
    public async Task Blame_names_each_line_s_path_in_its_commit_and_its_parent_there()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("old/a.cs", "one\ntwo\nthree\nfour\nfive\nsix\n", "First");
        repo.Git("mv", "old/a.cs", "b ä.cs");
        repo.Commit("b ä.cs", "one\ntwo\nthree\nFOUR\nfive\nsix\n", "Move and shout four");
        repo.Commit("c.cs", "created\n", "Add c");
        var git = new GitCli(new FileSystem());
        var ct = TestContext.Current.CancellationToken;

        var moved = (await git.BlameAsync(repo.File("b ä.cs"), 4, cancellationToken: ct)).Value!;
        var root = (await git.BlameAsync(repo.File("b ä.cs"), 1, cancellationToken: ct)).Value!;
        var created = (await git.BlameAsync(repo.File("c.cs"), 1, cancellationToken: ct)).Value!;

        Assert.Equal("b ä.cs", moved.Path);
        Assert.Equal((root.Hash, "old/a.cs"), (moved.Previous!.Hash, moved.Previous.Path));
        Assert.Equal("one\ntwo\nthree\nfour\nfive\nsix\n",
            (await git.ShowRepoFileAsync(repo.Path, moved.Previous.Path, moved.Previous.Hash, ct)).Value);
        Assert.Equal("old/a.cs", root.Path);
        Assert.Null(root.Previous);
        Assert.Equal("c.cs", created.Path);
        Assert.Null(created.Previous);
    }

    [Fact]
    public async Task Blame_outside_a_repo_fails()
    {
        using var dir = new TempRepo(init: false);
        dir.Write("a.cs", "one\n");
        var git = new GitCli(new FileSystem());

        var result = await git.BlameAsync(dir.File("a.cs"), 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("not a git repository", result.Error);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_git_that_hangs_times_out_with_a_failure()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs a shell script standing in for git");
        using var dir = new TempRepo(init: false);
        var script = dir.File("hanging-git");
        File.WriteAllText(script, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var git = new GitCli(new FileSystem(), executable: script, timeout: TimeSpan.FromMilliseconds(200));

        var stopwatch = Stopwatch.StartNew();
        var result = await git.GetRefsAsync(dir.Path, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
    }
}
