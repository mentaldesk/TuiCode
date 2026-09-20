using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Tests;

public class GitHubCliTests
{
    private const string NoCli = "Pull requests need the GitHub CLI: run gh auth login";

    [Fact]
    public void Parse_reads_the_pull_request_and_counts_its_checks_by_state()
    {
        const string json = """
        {
          "baseRefName": "main",
          "headRefName": "a-team/scopes",
          "number": 132,
          "title": "Command scopes should be fixed",
          "statusCheckRollup": [
            { "__typename": "CheckRun", "status": "COMPLETED", "conclusion": "SUCCESS" },
            { "__typename": "CheckRun", "status": "COMPLETED", "conclusion": "SKIPPED" },
            { "__typename": "CheckRun", "status": "COMPLETED", "conclusion": "FAILURE" },
            { "__typename": "CheckRun", "status": "IN_PROGRESS", "conclusion": "" },
            { "__typename": "StatusContext", "state": "PENDING" },
            { "__typename": "StatusContext", "state": "ERROR" }
          ]
        }
        """;

        var result = GitHubCli.Parse(json);

        Assert.Equal(
            new GitHubPullRequest(132, "Command scopes should be fixed", "main", "a-team/scopes", new GitHubChecks(2, 2, 2)),
            result.Value);
    }

    [Fact]
    public void Parse_of_a_pull_request_with_no_checks_counts_none()
    {
        var result = GitHubCli.Parse("""{"number":1,"title":"t","baseRefName":"main","headRefName":"f","statusCheckRollup":null}""");

        Assert.Equal(new GitHubChecks(0, 0, 0), result.Value?.Checks);
    }

    [Fact]
    public void Parse_of_something_that_isnt_a_pull_request_fails_rather_than_throws()
    {
        var result = GitHubCli.Parse("not json at all");

        Assert.False(result.Succeeded);
        Assert.False(result.CliUnavailable);
    }

    [Fact]
    public void ParseConversation_reads_the_description_then_each_comment_in_order()
    {
        const string json = """
        {
          "number": 185, "title": "Overview", "createdAt": "2026-09-20T06:54:00Z",
          "author": { "login": "jamescrosswell" }, "body": "What it does.",
          "comments": [
            { "author": { "login": "octocat" }, "createdAt": "2026-09-20T07:54:00Z", "body": "Looks good." },
            { "author": null, "createdAt": "nonsense", "body": "" }
          ]
        }
        """;

        var conversation = GitHubCli.ParseConversation(json).Value;

        Assert.Equal(185, conversation.Number);
        Assert.Equal("jamescrosswell", conversation.Author);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 6, 54, 0, TimeSpan.Zero), conversation.Date);
        Assert.Equal("What it does.", conversation.Body);
        Assert.Equal([("octocat", "Looks good."), ("", "")], conversation.Comments.Select(c => (c.Author, c.Body)));
    }

    [Fact]
    public void ParseConversation_of_something_that_isnt_a_pull_request_fails_rather_than_throws()
    {
        Assert.False(GitHubCli.ParseConversation("not json at all").Succeeded);
        Assert.False(GitHubCli.ParseConversation("{}").Succeeded);
    }

    [Fact]
    public void ParseList_reads_each_open_pull_request_and_flags_the_ones_awaiting_your_review()
    {
        const string json = """
        [
          { "number": 132, "title": "Command scopes should be fixed", "author": { "login": "jamescrosswell" } },
          { "number": 129, "title": "Delete, rename and move files", "author": { "login": "octocat" } }
        ]
        """;

        var result = GitHubCli.ParseList(json, """[{"number":129}]""");

        Assert.Equal(
        [
            new GitHubPullRequestSummary(132, "Command scopes should be fixed", "jamescrosswell"),
            new GitHubPullRequestSummary(129, "Delete, rename and move files", "octocat", ReviewRequested: true),
        ], result.Value);
    }

    [Fact]
    public void ParseList_keeps_the_branch_of_a_pull_request_from_this_repo_and_none_from_a_fork()
    {
        const string json = """
        [
          { "number": 132, "title": "t", "author": { "login": "a" }, "headRefName": "fix-scopes", "isCrossRepository": false },
          { "number": 129, "title": "t", "author": { "login": "b" }, "headRefName": "main", "isCrossRepository": true }
        ]
        """;

        var result = GitHubCli.ParseList(json, "[]");

        Assert.Equal(["fix-scopes", null], result.Value.Select(pr => pr.HeadBranch));
    }

    [Fact]
    public void ParseList_of_a_pull_request_with_no_author_leaves_the_author_blank()
    {
        var result = GitHubCli.ParseList("""[{"number":1,"title":"t","author":null}]""", "[]");

        Assert.Equal("", Assert.Single(result.Value).Author);
    }

    [Fact]
    public void ParseList_of_something_that_isnt_a_list_fails_rather_than_throws()
    {
        Assert.False(GitHubCli.ParseList("not json at all", "[]").Succeeded);
        Assert.False(GitHubCli.ParseList("[]", "not json at all").Succeeded);
    }

    [Fact]
    public void Interpret_treats_a_missing_gh_and_a_missing_login_as_the_CLI_being_unavailable()
    {
        Assert.Equal(NoCli, GitHubCli.Interpret(new CliRun(-1, "", "", "gh isn't installed or isn't on PATH", Missing: true)).Error);
        Assert.Equal(NoCli, GitHubCli.Interpret(new CliRun(4, "", "To get started with GitHub CLI, please run:  gh auth login", null)).Error);
        Assert.True(GitHubCli.Interpret(new CliRun(4, "", "", null)).CliUnavailable);
    }

    [Theory]
    [InlineData("no pull requests found for branch \"feature\"")]
    [InlineData("no git remotes found")]
    [InlineData("none of the git remotes configured for this repository point to a known GitHub host")]
    public void Interpret_treats_nothing_to_review_as_no_pull_request(string error)
    {
        var result = GitHubCli.Interpret(new CliRun(1, "", error, null));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Interpret_reports_any_other_failure_by_its_first_line()
    {
        var result = GitHubCli.Interpret(new CliRun(1, "", "HTTP 502: Bad gateway\nTry again later\n", null));

        Assert.Equal("HTTP 502: Bad gateway", result.Error);
        Assert.False(result.CliUnavailable);
    }

    [Fact]
    public async Task A_missing_gh_leaves_the_rest_of_the_Review_tab_working()
    {
        var gitHub = new GitHubCli(executable: "tuicode-no-such-gh");

        var result = await gitHub.GetPullRequestAsync(Path.GetTempPath(), TestContext.Current.CancellationToken);

        Assert.True(result.CliUnavailable);
        Assert.Equal(NoCli, result.Error);
    }

    [Fact]
    public async Task A_missing_gh_stops_the_list_and_the_checkout_too()
    {
        var gitHub = new GitHubCli(executable: "tuicode-no-such-gh");
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await gitHub.ListPullRequestsAsync(Path.GetTempPath(), ct)).CliUnavailable);
        Assert.True((await gitHub.CheckoutPullRequestAsync(Path.GetTempPath(), 1, ct)).CliUnavailable);
    }

    [Fact]
    public async Task A_repo_with_no_GitHub_remote_has_no_pull_request()
    {
        using var repo = new TempRepo(init: true);
        Assert.SkipUnless(GitHubCliAvailable.Value, "gh isn't on PATH, or isn't logged in");
        repo.Commit("a.txt", "alpha\n", "first");

        var result = await new GitHubCli().GetPullRequestAsync(repo.Path, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value);
    }

    private static readonly Lazy<bool> GitHubCliAvailable = new(() =>
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("gh", "auth status") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    });
}
