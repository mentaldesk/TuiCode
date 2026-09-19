using TuiCode.Abstractions;
using TuiCode.Workbench.Review;

namespace TuiCode.Tests;

public class BranchReviewTests
{
    private readonly FakeGitCli _git = new() { Root = "/work" };
    private readonly FakeGitHubCli _gitHub = new();

    [Fact]
    public void Header_reads_base_then_head_with_a_pull_request_and_the_branch_without_one()
    {
        Assert.Equal("feature ← origin/main  (no PR)", Review().Header);
        Assert.Equal("main ← feature", Review(PullRequest()).Header);
    }

    [Theory]
    [InlineData(11, 1, 2, "✓ 11  ✗ 1  ● 2 checks")]
    [InlineData(11, 1, 0, "✓ 11  ✗ 1 checks")]
    [InlineData(0, 0, 3, "● 3 checks")]
    [InlineData(0, 0, 0, "")]
    public void ChecksLine_leaves_out_the_states_with_nothing_in_them(int passed, int failed, int pending, string expected) =>
        Assert.Equal(expected, Review(PullRequest(checks: new GitHubChecks(passed, failed, pending))).ChecksLine);

    [Fact]
    public void ChecksLine_is_empty_without_a_pull_request() => Assert.Equal("", Review().ChecksLine);

    [Theory]
    [InlineData("#132 Short", 20, "#132 Short")]
    [InlineData("#132 Command scopes should be fixed", 20, "#132 Command scopes…")]
    [InlineData("#132 Command scopes should be fixed", 0, "#132 Command scopes should be fixed")]
    public void Truncate_ends_a_line_too_long_for_the_width_with_an_ellipsis(string text, int width, string expected) =>
        Assert.Equal(expected, BranchReview.Truncate(text, width));

    [Fact]
    public async Task WithPullRequestAsync_keeps_the_git_base_when_it_already_is_the_PRs_base()
    {
        _gitHub.PullRequest = PullRequest();

        var result = await BranchReview.WithPullRequestAsync(_git, _gitHub, Review(), TestContext.Current.CancellationToken);

        Assert.Equal(PullRequest(), result.Value?.PullRequest);
        Assert.Equal("origin/main", result.Value?.Base);
        Assert.Equal("b45e", result.Value?.MergeBase);
    }

    [Fact]
    public async Task WithPullRequestAsync_relists_the_files_against_the_PRs_own_base()
    {
        _gitHub.PullRequest = PullRequest(baseBranch: "release/1.0");
        _git.Resolvable.Add("origin/release/1.0");
        _git.MergeBases["origin/release/1.0"] = "cafe";
        _git.ChangesByRevision["cafe"] = [new GitChange(GitChangeKind.Added, "only-on-the-release-base.cs")];

        var result = await BranchReview.WithPullRequestAsync(_git, _gitHub, Review(), TestContext.Current.CancellationToken);

        Assert.Equal("origin/release/1.0", result.Value?.Base);
        Assert.Equal("cafe", result.Value?.MergeBase);
        Assert.Equal(["only-on-the-release-base.cs"], result.Value?.Changes.Select(c => c.Path));
        Assert.Equal("release/1.0 ← feature", result.Value?.Header);
    }

    [Fact]
    public async Task WithPullRequestAsync_says_so_when_the_PRs_base_isnt_in_the_checkout()
    {
        _gitHub.PullRequest = PullRequest(baseBranch: "release/1.0");

        var result = await BranchReview.WithPullRequestAsync(_git, _gitHub, Review(), TestContext.Current.CancellationToken);

        Assert.Null(result.Value);
        Assert.Contains("release/1.0", result.Error);
    }

    [Fact]
    public async Task WithPullRequestAsync_without_gh_reports_it_as_the_CLI_being_unavailable()
    {
        _gitHub.Missing = true;

        var result = await BranchReview.WithPullRequestAsync(_git, _gitHub, Review(), TestContext.Current.CancellationToken);

        Assert.True(result.CliUnavailable);
        Assert.Equal("Pull requests need the GitHub CLI: run gh auth login", result.Error);
    }

    [Fact]
    public async Task WithPullRequestAsync_without_a_pull_request_succeeds_with_nothing()
    {
        var result = await BranchReview.WithPullRequestAsync(_git, _gitHub, Review(), TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value);
    }

    private static BranchReview Review(GitHubPullRequest? pullRequest = null) =>
        new("/work", "feature", "origin/main", "b45e", [], pullRequest);

    private static GitHubPullRequest PullRequest(string baseBranch = "main", GitHubChecks checks = default) =>
        new(132, "Command scopes should be fixed", baseBranch, "feature", checks);
}
