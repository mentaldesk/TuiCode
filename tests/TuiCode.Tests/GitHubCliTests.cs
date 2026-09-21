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
          "headRefOid": "9a4f2c1",
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
            new GitHubPullRequest(132, "Command scopes should be fixed", "main", "a-team/scopes", new GitHubChecks(2, 2, 2), "9a4f2c1"),
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
    public void ParseThreads_reads_each_thread_with_its_line_resolution_and_comments()
    {
        const string json = """
        { "data": { "repository": { "pullRequest": { "reviewThreads": { "nodes": [
          { "isResolved": true, "isOutdated": false, "path": "src/a.cs", "line": 12, "diffSide": "RIGHT",
            "comments": { "nodes": [ { "author": { "login": "octocat" }, "createdAt": "2026-09-20T06:54:00Z", "body": "Why?" },
                                     { "author": { "login": "hubot" }, "createdAt": "2026-09-20T07:54:00Z", "body": "Because." } ] } },
          { "isResolved": false, "isOutdated": true, "path": "src/b.cs", "line": null, "diffSide": "RIGHT", "comments": { "nodes": [] } },
          { "isResolved": false, "isOutdated": false, "path": "src/c.cs", "line": 3, "diffSide": "LEFT", "comments": { "nodes": [] } }
        ] } } } } }
        """;

        var threads = GitHubCli.ParseThreads(json).Value;

        Assert.Equal([12, null, null], threads.Select(t => t.Line));
        Assert.Equal([false, true, true], threads.Select(t => t.Outdated));
        Assert.Equal([true, false, false], threads.Select(t => t.Resolved));
        Assert.Equal(1, threads[0].Replies);
        Assert.Equal("octocat · 2026-09-20 06:54", threads[0].First!.Heading);
    }

    [Fact]
    public void ParseThreads_of_an_answer_without_a_pull_request_fails_rather_than_throws()
    {
        Assert.False(GitHubCli.ParseThreads("not json at all").Succeeded);
        Assert.False(GitHubCli.ParseThreads("""{"data":{"repository":null}}""").Succeeded);
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

    [Theory]
    [InlineData(GitHubReviewVerdict.Comment, "--comment")]
    [InlineData(GitHubReviewVerdict.Approve, "--approve")]
    [InlineData(GitHubReviewVerdict.RequestChanges, "--request-changes")]
    public void ReviewArguments_asks_gh_for_the_verdict_and_passes_the_summary_as_the_body(GitHubReviewVerdict verdict, string flag)
    {
        var arguments = GitHubCli.ReviewArguments(132, verdict, "Looks good");

        Assert.Equal(["pr", "review", "132", flag, "--body", "Looks good"], arguments);
    }

    [Fact]
    public void ReviewArguments_leaves_an_empty_summary_out()
    {
        var arguments = GitHubCli.ReviewArguments(132, GitHubReviewVerdict.Approve, "");

        Assert.Equal(["pr", "review", "132", "--approve"], arguments);
    }

    [Fact]
    public void ReviewBody_posts_the_drafts_on_the_head_side_with_the_verdict_and_the_summary()
    {
        var body = GitHubCli.ReviewBody(GitHubReviewVerdict.RequestChanges, "Nearly there",
            [new DraftComment("src/a.cs", 12, "Rename this?")]);

        Assert.Equal(
            """{"event":"REQUEST_CHANGES","comments":[{"path":"src/a.cs","line":12,"side":"RIGHT","body":"Rename this?"}],"body":"Nearly there"}""",
            body);
    }

    [Fact]
    public void ReviewBody_leaves_an_empty_summary_out()
    {
        var body = GitHubCli.ReviewBody(GitHubReviewVerdict.Approve, "", [new DraftComment("src/a.cs", 12, "Rename this?")]);

        Assert.DoesNotContain("\"body\":\"\"", body);
        Assert.Contains("\"event\":\"APPROVE\"", body);
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
