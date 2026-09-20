using System.Diagnostics;
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

    public async Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        var listed = await RunAsync(repoRoot, ["pr", "list", "--state", "open", "--limit", $"{ListLimit}", "--json", "number,title,author"], _timeout, cancellationToken);
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
                return new GitHubPullRequestSummary(number, pr.GetProperty("title").GetString() ?? string.Empty, author ?? "", requested.Contains(number));
            })];
            return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Success(summaries);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure("gh answered with something we couldn't read");
        }
    }

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
