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

    public GitHubConversation? Conversation { get; set; }

    public string? ConversationError { get; set; }

    /// <summary>The PR numbers <see cref="GetConversationAsync"/> was asked about, in order.</summary>
    public List<int> ConversationCalls { get; } = [];

    /// <summary>Set to hold the PR lookup until the test lets it finish, as a slow <c>gh</c> does (#228).</summary>
    public TaskCompletionSource? HoldPullRequest { get; set; }

    public async Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (HoldPullRequest is { } held) await held.Task.ConfigureAwait(false);
        if (Missing) return GitHubResult<GitHubPullRequest?>.NoCli();
        return Error is { } error
            ? GitHubResult<GitHubPullRequest?>.Failure(error)
            : GitHubResult<GitHubPullRequest?>.Success(PullRequest);
    }

    public Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        if (Missing) return Task.FromResult(GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.NoCli());
        return Task.FromResult(Error is { } error
            ? GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Failure(error)
            : GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>.Success(OpenPullRequests));
    }

    public Task<GitHubResult<GitHubConversation>> GetConversationAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        ConversationCalls.Add(number);
        if (Missing) return Task.FromResult(GitHubResult<GitHubConversation>.NoCli());
        return Task.FromResult(ConversationError is { } error
            ? GitHubResult<GitHubConversation>.Failure(error)
            : GitHubResult<GitHubConversation>.Success(
                Conversation ?? new GitHubConversation(number, $"#{number}", "octocat", default, string.Empty, [])));
    }

    public string? ReviewError { get; set; }

    /// <summary>The reviews <see cref="SubmitReviewAsync"/> was asked to submit, in order.</summary>
    public List<(string RepoRoot, int Number, GitHubReviewVerdict Verdict, string Summary)> Reviews { get; } = [];

    public Task<GitHubResult<bool>> SubmitReviewAsync(
        string repoRoot, int number, GitHubReviewVerdict verdict, string summary, CancellationToken cancellationToken = default)
    {
        Reviews.Add((repoRoot, number, verdict, summary));
        return Task.FromResult(ReviewError is { } error
            ? GitHubResult<bool>.Failure(error)
            : GitHubResult<bool>.Success(true));
    }

    public IReadOnlyList<GitHubReviewThread> ReviewThreads { get; set; } = [];

    public string? ThreadsError { get; set; }

    /// <summary>Held open to keep threads pending while a test looks at the diff without them (#186).</summary>
    public Task ThreadsGate { get; set; } = Task.CompletedTask;

    public async Task<GitHubResult<IReadOnlyList<GitHubReviewThread>>> GetReviewThreadsAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        await ThreadsGate;
        if (Missing) return GitHubResult<IReadOnlyList<GitHubReviewThread>>.NoCli();
        return ThreadsError is { } error
            ? GitHubResult<IReadOnlyList<GitHubReviewThread>>.Failure(error)
            : GitHubResult<IReadOnlyList<GitHubReviewThread>>.Success(ReviewThreads);
    }

    public Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default)
    {
        Checkouts.Add((worktreePath, number));
        return Task.FromResult(CheckoutError is { } error
            ? GitHubResult<bool>.Failure(error)
            : GitHubResult<bool>.Success(true));
    }
}
