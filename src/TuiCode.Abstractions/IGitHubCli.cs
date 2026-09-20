namespace TuiCode.Abstractions;

/// <summary>
/// Read-only queries against the <c>gh</c> CLI, so TuiCode never handles a token and GitHub
/// Enterprise works wherever <c>gh</c> does. Every call returns a <see cref="GitHubResult{T}"/>:
/// a missing <c>gh</c>, a non-zero exit or a timeout is a failure, never an exception.
/// </summary>
public interface IGitHubCli
{
    /// <summary>The open PR for the branch checked out in <paramref name="repoRoot"/>, or null when there's none.</summary>
    Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default);

    /// <summary>PR <paramref name="number"/>'s description and the comments on it, oldest first (#185).</summary>
    Task<GitHubResult<GitHubConversation>> GetConversationAsync(string repoRoot, int number, CancellationToken cancellationToken = default);
}

public readonly record struct GitHubResult<T>(T Value, string? Error, bool CliUnavailable = false)
{
    public bool Succeeded => Error is null;

    public static GitHubResult<T> Success(T value) => new(value, null);

    public static GitHubResult<T> Failure(string error) => new(default!, error);

    /// <summary><c>gh</c> is missing or isn't logged in: everything that needs only git still works.</summary>
    public static GitHubResult<T> NoCli() => new(default!, "Pull requests need the GitHub CLI: run gh auth login", true);
}

public sealed record GitHubPullRequest(int Number, string Title, string BaseBranch, string HeadBranch, GitHubChecks Checks);

/// <summary>A PR as the Overview tab reads it (#185): what it says it does, then what's been said about it.</summary>
public sealed record GitHubConversation(
    int Number,
    string Title,
    string Author,
    DateTimeOffset Date,
    string Body,
    IReadOnlyList<GitHubComment> Comments);

/// <summary>One comment on a PR, posted at <paramref name="Date"/>.</summary>
public sealed record GitHubComment(string Author, DateTimeOffset Date, string Body);

/// <summary>A PR's checks, counted by state.</summary>
public readonly record struct GitHubChecks(int Passed, int Failed, int Pending)
{
    public int Total => Passed + Failed + Pending;
}
