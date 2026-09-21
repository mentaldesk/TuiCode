using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

public sealed class GitHubCli(string executable = "gh", TimeSpan? timeout = null) : IGitHubCli
{
    /// <summary>gh's exit code when there's no token for the host.</summary>
    private const int AuthExitCode = 4;

    /// <summary>How many open PRs the <c>opr</c> picker lists.</summary>
    internal const int ListLimit = 100;

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    /// <summary>A checkout fetches the PR, so it gets far longer than a query.</summary>
    private static readonly TimeSpan CheckoutTimeout = TimeSpan.FromMinutes(2);

    public async Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default) =>
        Interpret(await RunAsync(repoRoot, ["pr", "view", "--json", "number,title,baseRefName,headRefName,statusCheckRollup"], _timeout, cancellationToken));

    public async Task<GitHubResult<GitHubConversation>> GetConversationAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(repoRoot, ["pr", "view", $"{number}", "--json", "number,title,author,createdAt,body,comments"], _timeout, cancellationToken);
        if (Unavailable<GitHubConversation>(run) is { } failed) return failed;
        return run.ExitCode == 0 ? ParseConversation(run.Output) : GitHubResult<GitHubConversation>.Failure(ErrorMessage(run));
    }

    /// <summary>
    /// Review threads come from GraphQL: the REST API knows nothing about whether a thread is resolved.
    /// <c>{owner}</c> and <c>{repo}</c> are gh's own placeholders for the repo the command runs in.
    /// </summary>
    public async Task<GitHubResult<IReadOnlyList<GitHubReviewThread>>> GetReviewThreadsAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(repoRoot,
            ["api", "graphql", "-F", "owner={owner}", "-F", "name={repo}", "-F", $"number={number}", "-f", $"query={ThreadsQuery}"],
            _timeout, cancellationToken);
        if (Unavailable<IReadOnlyList<GitHubReviewThread>>(run) is { } failed) return failed;
        return run.ExitCode == 0 ? ParseThreads(run.Output) : GitHubResult<IReadOnlyList<GitHubReviewThread>>.Failure(ErrorMessage(run));
    }

    private const string ThreadsQuery =
        "query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name)" +
        "{pullRequest(number:$number){reviewThreads(first:100){nodes{isResolved isOutdated path line diffSide " +
        "comments(first:100){nodes{author{login} body createdAt}}}}}}}";

    internal static GitHubResult<IReadOnlyList<GitHubReviewThread>> ParseThreads(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var nodes = document.RootElement
                .GetProperty("data").GetProperty("repository").GetProperty("pullRequest")
                .GetProperty("reviewThreads").GetProperty("nodes");
            IReadOnlyList<GitHubReviewThread> threads = [.. nodes.EnumerateArray().Select(thread =>
            {
                // A thread on the base side names a line of the base file, which is no line of the head: outdated here too.
                var onHead = Text(thread, "diffSide") is null or "RIGHT";
                var line = onHead && thread.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : (int?)null;
                return new GitHubReviewThread(
                    Text(thread, "path") ?? string.Empty,
                    line,
                    Flag(thread, "isResolved"),
                    line is null || Flag(thread, "isOutdated"),
                    Comments(thread));
            })];
            return GitHubResult<IReadOnlyList<GitHubReviewThread>>.Success(threads);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return GitHubResult<IReadOnlyList<GitHubReviewThread>>.Failure("gh answered with something we couldn't read");
        }
    }

    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<GitHubComment> Comments(JsonElement element)
    {
        if (!element.TryGetProperty("comments", out var comments)
            || !comments.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
            return [];
        return [.. nodes.EnumerateArray().Select(c => new GitHubComment(Login(c), Date(c), Text(c, "body") ?? string.Empty))];
    }

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        var listed = await RunAsync(repoRoot,
            ["pr", "list", "--state", "open", "--limit", $"{ListLimit}", "--json", "number,title,author,headRefName,isCrossRepository"],
            _timeout, cancellationToken);
        if (Unavailable<IReadOnlyList<GitHubPullRequestSummary>>(listed) is { } failed) return failed;
        if (listed.ExitCode != 0) return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure(ErrorMessage(listed));

        // `@me` in a search is resolved by gh itself, so a review requested of a team the user is in counts too.
        var requested = await RunAsync(repoRoot,
            ["pr", "list", "--state", "open", "--limit", $"{ListLimit}", "--search", "review-requested:@me", "--json", "number"],
            _timeout, cancellationToken);
        if (Unavailable<IReadOnlyList<GitHubPullRequestSummary>>(requested) is { } searchFailed) return searchFailed;
        if (requested.ExitCode != 0) return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure(ErrorMessage(requested));

        return ParseList(listed.Output, requested.Output);
    }

    public async Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(worktreePath, ["pr", "checkout", $"{number}"], CheckoutTimeout, cancellationToken);
        if (Unavailable<bool>(run) is { } failed) return failed;
        return run.ExitCode == 0 ? GitHubResult<bool>.Success(true) : GitHubResult<bool>.Failure(ErrorMessage(run));
    }

    public async Task<GitHubResult<bool>> SubmitReviewAsync(
        string repoRoot, int number, GitHubReviewVerdict verdict, string summary, CancellationToken cancellationToken = default)
    {
        var run = await RunAsync(repoRoot, ReviewArguments(number, verdict, summary), _timeout, cancellationToken);
        if (Unavailable<bool>(run) is { } failed) return failed;
        return run.ExitCode == 0 ? GitHubResult<bool>.Success(true) : GitHubResult<bool>.Failure(ErrorMessage(run));
    }

    /// <summary>An empty summary is left out rather than sent as one, which gh rejects.</summary>
    internal static string[] ReviewArguments(int number, GitHubReviewVerdict verdict, string summary)
    {
        var flag = verdict switch
        {
            GitHubReviewVerdict.Approve => "--approve",
            GitHubReviewVerdict.RequestChanges => "--request-changes",
            _ => "--comment",
        };
        string[] review = ["pr", "review", $"{number}", flag];
        return summary.Length == 0 ? review : [.. review, "--body", summary];
    }

    private Task<CliRun> RunAsync(string workingDirectory, string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        // Nothing here can answer a prompt, and an unanswered one would sit until the timeout.
        info.Environment["GH_PROMPT_DISABLED"] = "1";

        return CliProcess.RunAsync("gh", info, timeout, cancellationToken);
    }

    /// <summary>The result for a run that never reached GitHub, or null when it did.</summary>
    private static GitHubResult<T>? Unavailable<T>(CliRun run) =>
        run.Missing || run.ExitCode == AuthExitCode ? GitHubResult<T>.NoCli()
        : run.Failure is { } failure ? GitHubResult<T>.Failure(failure)
        : null;

    /// <summary>Open PRs as <c>gh pr list</c> gives them, flagged by the numbers the review-requested search returned.</summary>
    internal static GitHubResult<IReadOnlyList<GitHubPullRequestSummary>> ParseList(string json, string requestedJson)
    {
        try
        {
            using var requestedDocument = JsonDocument.Parse(requestedJson);
            var requested = requestedDocument.RootElement.EnumerateArray()
                .Select(pr => pr.GetProperty("number").GetInt32())
                .ToHashSet();

            using var document = JsonDocument.Parse(json);
            IReadOnlyList<GitHubPullRequestSummary> summaries = [.. document.RootElement.EnumerateArray().Select(pr =>
            {
                var number = pr.GetProperty("number").GetInt32();
                var author = pr.TryGetProperty("author", out var a) ? Text(a, "login") : null;
                return new GitHubPullRequestSummary(number, pr.GetProperty("title").GetString() ?? string.Empty, author ?? "",
                    HeadBranch(pr), requested.Contains(number));
            })];
            return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Success(summaries);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure("gh answered with something we couldn't read");
        }
    }

    /// <summary>The PR's branch, or null when it comes from a fork and so names nothing in this repo.</summary>
    private static string? HeadBranch(JsonElement pullRequest) =>
        pullRequest.TryGetProperty("isCrossRepository", out var fork) && fork.GetBoolean() ? null
        : pullRequest.TryGetProperty("headRefName", out var head) ? head.GetString()
        : null;

    /// <summary>gh's answer as a result: no PR, no <c>gh</c> to ask, or the PR.</summary>
    internal static GitHubResult<GitHubPullRequest?> Interpret(CliRun run)
    {
        if (Unavailable<GitHubPullRequest?>(run) is { } failed)
            return failed;
        if (run.ExitCode == 0)
            return Parse(run.Output);
        // A branch with no PR, and a repo GitHub doesn't host, both mean "nothing to review against".
        foreach (var quiet in (string[])["no pull requests found", "no git remotes found", "known GitHub host"])
            if (run.Error.Contains(quiet, StringComparison.Ordinal))
                return GitHubResult<GitHubPullRequest?>.Success(null);
        return GitHubResult<GitHubPullRequest?>.Failure(ErrorMessage(run));
    }

    internal static GitHubResult<GitHubPullRequest?> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return GitHubResult<GitHubPullRequest?>.Success(new GitHubPullRequest(
                root.GetProperty("number").GetInt32(),
                root.GetProperty("title").GetString() ?? string.Empty,
                root.GetProperty("baseRefName").GetString() ?? string.Empty,
                root.GetProperty("headRefName").GetString() ?? string.Empty,
                CountChecks(root)));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return GitHubResult<GitHubPullRequest?>.Failure("gh answered with something we couldn't read");
        }
    }

    internal static GitHubResult<GitHubConversation> ParseConversation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            IReadOnlyList<GitHubComment> comments = root.TryGetProperty("comments", out var listed) && listed.ValueKind == JsonValueKind.Array
                ? [.. listed.EnumerateArray().Select(c => new GitHubComment(Login(c), Date(c), Text(c, "body") ?? string.Empty))]
                : [];
            return GitHubResult<GitHubConversation>.Success(new GitHubConversation(
                root.GetProperty("number").GetInt32(),
                Text(root, "title") ?? string.Empty,
                Login(root),
                Date(root),
                Text(root, "body") ?? string.Empty,
                comments));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return GitHubResult<GitHubConversation>.Failure("gh answered with something we couldn't read");
        }
    }

    /// <summary>Who wrote it; gh reports a deleted account as a null author.</summary>
    private static string Login(JsonElement element) =>
        (element.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
            ? Text(author, "login")
            : null) ?? string.Empty;

    private static DateTimeOffset Date(JsonElement element) =>
        Text(element, "createdAt") is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : default;

    private static GitHubChecks CountChecks(JsonElement pullRequest)
    {
        if (!pullRequest.TryGetProperty("statusCheckRollup", out var rollup) || rollup.ValueKind != JsonValueKind.Array)
            return default;

        int passed = 0, failed = 0, pending = 0;
        foreach (var check in rollup.EnumerateArray())
        {
            // A CheckRun reports a conclusion only once it's COMPLETED; a StatusContext has a state throughout.
            var state = Text(check, "status") is "COMPLETED" or null ? Text(check, "conclusion") ?? Text(check, "state") : null;
            switch (state)
            {
                case "SUCCESS" or "NEUTRAL" or "SKIPPED": passed++; break;
                case null or "" or "PENDING" or "EXPECTED" or "QUEUED" or "IN_PROGRESS" or "WAITING" or "REQUESTED": pending++; break;
                default: failed++; break;
            }
        }
        return new GitHubChecks(passed, failed, pending);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ErrorMessage(CliRun run)
    {
        var line = run.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line ?? $"gh exited with code {run.ExitCode}";
    }
}
