namespace TuiCode.Abstractions;

/// <summary>
/// The <c>gh</c> CLI, so TuiCode never handles a token and GitHub
/// Enterprise works wherever <c>gh</c> does. Every call returns a <see cref="GitHubResult{T}"/>:
/// a missing <c>gh</c>, a non-zero exit or a timeout is a failure, never an exception.
/// </summary>
public interface IGitHubCli
{
    /// <summary>The open PR for the branch checked out in <paramref name="repoRoot"/>, or null when there's none.</summary>
    Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default);

    /// <summary>The repo's open PRs, newest first, each flagged when the logged-in user's review is requested.</summary>
    Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks PR <paramref name="number"/> out in <paramref name="worktreePath"/>, fetching it first, so that
    /// <see cref="GetPullRequestAsync"/> there finds it again. True once it's checked out.
    /// </summary>
    Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default);
}

public readonly record struct GitHubResult<T>(T Value, string? Error, bool CliUnavailable = false)
{
    public bool Succeeded => Error is null;

    public static GitHubResult<T> Success(T value) => new(value, null);

    public static GitHubResult<T> Failure(string error) => new(default!, error);

    /// <summary><c>gh</c> is missing or isn't logged in: everything that needs only git still works.</summary>
    public static GitHubResult<T> NoCli() => new(default!, "Pull requests need the GitHub CLI: run gh auth login", true);
}

/// <summary>
/// A PR as the <c>opr</c> picker lists it. <c>HeadBranch</c> is null for a PR from a fork, whose branch name
/// means nothing here and can collide with an unrelated local branch.
/// </summary>
public sealed record GitHubPullRequestSummary(int Number, string Title, string Author, string? HeadBranch = null, bool ReviewRequested = false);

public sealed record GitHubPullRequest(int Number, string Title, string BaseBranch, string HeadBranch, GitHubChecks Checks);

/// <summary>A PR's checks, counted by state.</summary>
public readonly record struct GitHubChecks(int Passed, int Failed, int Pending)
{
    public int Total => Passed + Failed + Pending;
}
