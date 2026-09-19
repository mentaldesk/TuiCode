using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeGitHubCli : IGitHubCli
{
    public GitHubPullRequest? PullRequest { get; set; }
    public string? Error { get; set; }
    public bool Missing { get; set; }
    public int Calls { get; private set; }

    public Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Missing) return Task.FromResult(GitHubResult<GitHubPullRequest?>.NoCli());
        return Task.FromResult(Error is { } error
            ? GitHubResult<GitHubPullRequest?>.Failure(error)
            : GitHubResult<GitHubPullRequest?>.Success(PullRequest));
    }
}
