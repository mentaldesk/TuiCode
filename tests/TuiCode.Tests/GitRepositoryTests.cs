using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class GitRepositoryTests
{
    private readonly MockFileSystem _fs = new();

    [Fact]
    public void A_plain_checkout_keeps_HEAD_and_its_branches_in_one_git_dir()
    {
        _fs.AddDirectory(Full("/repo/.git"));
        _fs.AddDirectory(Full("/repo/src"));

        Assert.Equal(new GitDirs(Full("/repo/.git"), Full("/repo/.git")), GitRepository.Dirs(Folder("/repo/src")));
    }

    [Fact]
    public void A_submodule_s_git_file_is_read_relative_to_where_it_is()
    {
        _fs.AddFile(Full("/repo/lib/.git"), new MockFileData("gitdir: ../.git/modules/lib\n"));
        _fs.AddDirectory(Full("/repo/.git/modules/lib"));

        var dirs = GitRepository.Dirs(Folder("/repo/lib"));

        Assert.Equal(new GitDirs(Full("/repo/.git/modules/lib"), Full("/repo/.git/modules/lib")), dirs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a git file\n")]
    public void A_git_file_that_doesn_t_say_where_to_look_gives_no_dirs(string content)
    {
        _fs.AddFile(Full("/repo/.git"), new MockFileData(content));

        Assert.Null(GitRepository.Dirs(Folder("/repo")));
        Assert.True(GitRepository.Contains(Folder("/repo")));
    }

    [Fact]
    public void Outside_a_repo_there_are_no_dirs()
    {
        _fs.AddDirectory(Full("/elsewhere"));

        Assert.Null(GitRepository.Dirs(Folder("/elsewhere")));
    }

    [Fact]
    public void A_linked_worktree_has_its_own_HEAD_and_shares_the_main_checkout_s_branches()
    {
        using var repo = new TempRepo(init: true);
        repo.Commit("a.cs", "one\n", "First");
        repo.Git("worktree", "add", "-q", "-b", "feature", repo.File("feature"));
        var fs = new FileSystem();

        var main = GitRepository.Dirs(fs.DirectoryInfo.New(repo.Path))!;
        var linked = GitRepository.Dirs(fs.DirectoryInfo.New(repo.File("feature")))!;

        Assert.Equal("ref: refs/heads/feature", File.ReadAllText(Path.Combine(linked.GitDir, "HEAD")).Trim());
        Assert.True(File.Exists(Path.Combine(linked.CommonDir, "refs", "heads", "feature")));
        Assert.Equal(File.ReadAllText(Path.Combine(main.CommonDir, "config")), File.ReadAllText(Path.Combine(linked.CommonDir, "config")));
        Assert.NotEqual(main.GitDir, linked.GitDir);
    }

    private IDirectoryInfo Folder(string path) => _fs.DirectoryInfo.New(Full(path));

    private string Full(string path) => _fs.Path.GetFullPath(path);
}
