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

    private sealed class TempRepo : IDisposable
    {
        public TempRepo(bool init)
        {
            Assert.SkipUnless(GitAvailable.Value, "git isn't on PATH");
            Path = Directory.CreateTempSubdirectory("tuicode-git-").FullName;
            if (init)
                Git("init", "-q", "-b", "main");
        }

        public string Path { get; }

        public string File(string relative) => System.IO.Path.Combine(Path, relative);

        public void Commit(string relative, string content, string message)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(File(relative))!);
            System.IO.File.WriteAllText(File(relative), content);
            Git("add", relative);
            Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
                "commit", "-q", "-m", message);
        }

        public void Git(params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(Path);
            foreach (var argument in arguments)
                info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
        }

        public void Dispose()
        {
            // Git's object files are read-only, which stops Directory.Delete on Windows.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }

        private static readonly Lazy<bool> GitAvailable = new(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true })!;
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        });
    }
}
