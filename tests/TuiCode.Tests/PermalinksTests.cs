using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

// #623.
public class PermalinksTests
{
    private const string Head = "4a91c0e2b7d1f3e5a6c8b9d0e1f2a3b4c5d6e7f8";
    private const string Older = "1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e";
    private const string Committed = "one\ntwo\nthree\nfour\nfive\n";

    private readonly MockFileSystem _fs = new();
    private readonly FakeGitCli _git = new();
    private readonly string _file;

    public PermalinksTests()
    {
        _git.Root = _fs.Path.GetFullPath("/work");
        _file = _fs.Path.GetFullPath("/work/src/a.cs");
        _git.RemoteUrl = "git@github.com:o/r.git";
        _git.Pushed["HEAD"] = new GitPushedCommit(Head, IsRevision: true);
        _git.RepoFiles[$"{Head}:src/a.cs"] = Committed;
        _git.RepoFiles[$"HEAD:src/a.cs"] = Committed;
    }

    [Theory]
    [InlineData("git@github.com:o/r.git")]
    [InlineData("git@github.com:o/r")]
    [InlineData("ssh://git@github.com/o/r")]
    [InlineData("ssh://git@github.com:22/o/r.git")]
    [InlineData("https://github.com/o/r")]
    [InlineData("https://github.com/o/r.git")]
    [InlineData("https://me@github.com/o/r.git/")]
    [InlineData("http://github.com/o/r")]
    public void A_GitHub_remote_in_any_form_gives_the_same_repo(string url) =>
        Assert.Equal(new GitHubRepo("github.com", "o", "r"), GitHubRepo.Parse(url));

    [Theory]
    [InlineData("git@github.example.com:o/r.git")]
    [InlineData("https://github.example.com/o/r")]
    public void An_Enterprise_host_keeps_its_name(string url) =>
        Assert.Equal(new GitHubRepo("github.example.com", "o", "r"), GitHubRepo.Parse(url));

    [Theory]
    [InlineData("git@gitlab.com:o/r.git")]
    [InlineData("https://gitlab.example.com/o/r")]
    [InlineData("git@bitbucket.org:o/r.git")]
    [InlineData("https://dev.azure.com/o/p/_git/r")]
    [InlineData("git@ssh.dev.azure.com:v3/o/p/r")]
    [InlineData("https://o.visualstudio.com/p/_git/r")]
    [InlineData("/srv/git/r.git")]
    [InlineData("file:///srv/git/r.git")]
    [InlineData("https://github.com/o")]
    public void Other_hosts_and_paths_are_not_GitHub(string url) =>
        Assert.Null(GitHubRepo.Parse(url));

    [Fact]
    public void A_path_is_escaped_segment_by_segment() =>
        Assert.Equal("https://github.com/o/r/blob/abc/src/a%20b%23.cs#L3-L5", new GitHubRepo("github.com", "o", "r").Blob("abc", "src/a b#.cs", 2, 4));

    [Theory]
    [InlineData("one\ntwo\nthree\nfour\nfive\n", 3, 3, 3, false)]
    [InlineData("new\none\ntwo\nthree\nfour\nfive\n", 4, 4, 3, false)]
    [InlineData("two\nthree\nfour\nfive\n", 2, 2, 3, false)]
    [InlineData("one\ntwo\nadded\nthree\nfour\nfive\n", 2, 2, 1, true)]
    [InlineData("added\none\ntwo\nthree\nfour\nfive\n", 0, 0, 0, true)]
    [InlineData("one\ntwo\nTHREE\nfour\nfive\n", 2, 2, 2, false)]
    public void A_line_maps_back_to_where_it_is_in_the_commit(string current, int first, int last, int mapped, bool isNew) =>
        Assert.Equal((mapped, mapped, isNew), Permalinks.MapLines(Lines(Committed), Lines(current), first, last));

    [Fact]
    public void A_selection_spanning_added_lines_maps_each_end() =>
        Assert.Equal((1, 3, false), Permalinks.MapLines(Lines(Committed), Lines("one\ntwo\nadded\nmore\nthree\nfour\nfive\n"), 1, 5));

    [Fact]
    public async Task A_clean_line_at_a_pushed_HEAD_links_to_HEAD()
    {
        var link = await Build(Committed, 2, 2);

        Assert.Equal($"https://github.com/o/r/blob/{Head}/src/a.cs#L3", link.Url);
        Assert.Equal("permalink to line 3", link.Message);
    }

    [Fact]
    public async Task A_selection_links_to_its_lines()
    {
        var link = await Build(Committed, 1, 3);

        Assert.EndsWith("#L2-L4", link.Url);
        Assert.Equal("permalink to lines 2–4", link.Message);
    }

    [Fact]
    public async Task An_unpushed_HEAD_links_to_the_newest_pushed_commit_and_names_it()
    {
        _git.Pushed["HEAD"] = new GitPushedCommit(Older, IsRevision: false);
        _git.RepoFiles[$"{Older}:src/a.cs"] = Committed;

        var link = await Build(Committed, 2, 2);

        Assert.Equal($"https://github.com/o/r/blob/{Older}/src/a.cs#L3", link.Url);
        Assert.Equal("permalink to line 3 at 1d2e3f4, the newest pushed commit", link.Message);
    }

    [Fact]
    public async Task A_shifted_line_says_which_commit_its_number_is_in()
    {
        var link = await Build("new\n" + Committed, 3, 3);

        Assert.EndsWith("#L3", link.Url);
        Assert.Equal("permalink to line 3 at 4a91c0e", link.Message);
    }

    [Fact]
    public async Task An_added_line_links_to_the_nearest_earlier_line_and_says_so()
    {
        var link = await Build("one\ntwo\nadded\nthree\nfour\nfive\n", 2, 2);

        Assert.EndsWith("#L2", link.Url);
        Assert.Equal("permalink to line 2 at 4a91c0e (line 3 is new since then)", link.Message);
    }

    [Fact]
    public async Task A_side_showing_a_pushed_commit_links_to_it_directly()
    {
        _git.Pushed[Older] = new GitPushedCommit(Older, IsRevision: true);
        _git.RepoFiles[$"{Older}:old/a.cs"] = "x\ny\n";

        var link = await Permalinks.BuildAsync(_git, _fs, new PermalinkTarget(_file, Lines("x\ny\n"), 1, 1, Older, "old/a.cs"));

        Assert.Equal($"https://github.com/o/r/blob/{Older}/old/a.cs#L2", link.Url);
        Assert.Equal("permalink to line 2", link.Message);
    }

    [Fact]
    public async Task An_Enterprise_remote_links_to_its_host()
    {
        _git.RemoteUrl = "https://github.example.com/o/r.git";

        var link = await Build(Committed, 0, 0);

        Assert.StartsWith("https://github.example.com/o/r/blob/", link.Url);
    }

    [Fact]
    public async Task Outside_a_repo_there_is_no_link() =>
        await Refused(() => _git.Root = null, "a.cs isn't in a git repository.");

    [Fact]
    public async Task Without_a_remote_there_is_no_link() =>
        await Refused(() => _git.RemoteUrl = null, "The repository has no remote to link to.");

    [Fact]
    public async Task A_remote_that_is_not_GitHub_gives_no_link() =>
        await Refused(() => _git.RemoteUrl = "git@gitlab.com:o/r.git", "git@gitlab.com:o/r.git isn't a GitHub remote.");

    [Fact]
    public async Task With_nothing_pushed_there_is_no_link() =>
        await Refused(() => _git.Pushed.Clear(), "a.cs isn't in any pushed commit yet.");

    [Fact]
    public async Task A_file_committed_but_not_pushed_gives_no_link() =>
        await Refused(() => _git.RepoFiles.Remove($"{Head}:src/a.cs"), "a.cs isn't in any pushed commit yet.");

    [Fact]
    public async Task An_untracked_file_gives_no_link() =>
        await Refused(() => _git.RepoFiles.Clear(), "a.cs isn't tracked by git.");

    private async Task Refused(Action arrange, string message)
    {
        arrange();
        var link = await Build(Committed, 0, 0);
        Assert.Null(link.Url);
        Assert.Equal(message, link.Message);
    }

    private Task<Permalink> Build(string current, int first, int last) =>
        Permalinks.BuildAsync(_git, _fs, new PermalinkTarget(_file, Lines(current), first, last));

    private static IReadOnlyList<string> Lines(string text) => TuiCode.Editor.DiffTab.SplitLines(text);
}
