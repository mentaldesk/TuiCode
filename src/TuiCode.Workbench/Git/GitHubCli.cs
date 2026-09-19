using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

public sealed class GitHubCli(string executable = "gh", TimeSpan? timeout = null) : IGitHubCli
{
    /// <summary>gh's exit code when there's no token for the host.</summary>
    private const int AuthExitCode = 4;

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public async Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in (string[])["pr", "view", "--json", "number,title,baseRefName,headRefName,statusCheckRollup"])
            info.ArgumentList.Add(argument);
        // Nothing here can answer a prompt, and an unanswered one would sit until the timeout.
        info.Environment["GH_PROMPT_DISABLED"] = "1";

        var run = await CliProcess.RunAsync("gh", info, _timeout, cancellationToken);
        return Interpret(run);
    }

    /// <summary>gh's answer as a result: no PR, no <c>gh</c> to ask, or the PR.</summary>
    internal static GitHubResult<GitHubPullRequest?> Interpret(CliRun run)
    {
        if (run.Missing || run.ExitCode == AuthExitCode)
            return GitHubResult<GitHubPullRequest?>.NoCli();
        if (run.Failure is { } failure)
            return GitHubResult<GitHubPullRequest?>.Failure(failure);
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
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ErrorMessage(CliRun run)
    {
        var line = run.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line ?? $"gh exited with code {run.ExitCode}";
    }
}
