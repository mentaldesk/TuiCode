using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeGitHubCli : IGitHubCli
{
    public GitHubPullRequest? PullRequest { get; set; }
    public string? Error { get; set; }
    public bool Missing { get; set; }
    public int Calls { get; private set; }

    public IReadOnlyList<GitHubPullRequestSummary> OpenPullRequests { get; set; } = [];
    public string? CheckoutError { get; set; }

    /// <summary>The (worktree, number) pairs <see cref="CheckoutPullRequestAsync"/> was called with, in order.</summary>
    public List<(string Worktree, int Number)> Checkouts { get; } = [];

    public Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Missing) return Task.FromResult(GitHubResult<GitHubPullRequest?>.NoCli());
        return Task.FromResult(Error is { } error
            ? GitHubResult<GitHubPullRequest?>.Failure(error)
            : GitHubResult<GitHubPullRequest?>.Success(PullRequest));
    }

    public Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        if (Missing) return Task.FromResult(GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.NoCli());
        return Task.FromResult(Error is { } error
            ? GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure(error)
            : GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Success(OpenPullRequests));
    }

    public Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default)
    {
        Checkouts.Add((worktreePath, number));
        return Task.FromResult(CheckoutError is { } error
            ? GitHubResult<bool>.Failure(error)
            : GitHubResult<bool>.Success(true));
    }
}
