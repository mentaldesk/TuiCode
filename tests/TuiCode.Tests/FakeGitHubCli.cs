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

    /// <summary>The draft comments submitted with each of those reviews, in the same order.</summary>
    public List<IReadOnlyList<DraftComment>> ReviewComments { get; } = [];

    public Task<GitHubResult<bool>> SubmitReviewAsync(
        string repoRoot, int number, GitHubReviewVerdict verdict, string summary, IReadOnlyList<DraftComment> comments,
        CancellationToken cancellationToken = default)
    {
        Reviews.Add((repoRoot, number, verdict, summary));
        ReviewComments.Add(comments);
        return Task.FromResult(ReviewError is { } error
            ? GitHubResult<bool>.Failure(error)
            : GitHubResult<bool>.Success(true));
    }

    public string? ReplyError { get; set; }

    /// <summary>What <see cref="ReplyToThreadAsync"/> hands back, standing in for GitHub's own record of it.</summary>
    public GitHubComment Reply { get; set; } = new("octocat", default, "Replied");

    /// <summary>The replies <see cref="ReplyToThreadAsync"/> was asked to post, in order.</summary>
    public List<(string RepoRoot, int Number, long ReplyToId, string Body)> Replies { get; } = [];

    public Task<GitHubResult<GitHubComment>> ReplyToThreadAsync(
        string repoRoot, int number, long replyToId, string body, CancellationToken cancellationToken = default)
    {
        Replies.Add((repoRoot, number, replyToId, body));
        return Task.FromResult(ReplyError is { } error
            ? GitHubResult<GitHubComment>.Failure(error)
            : GitHubResult<GitHubComment>.Success(Reply with { Body = body }));
    }

    public IReadOnlyList<GitHubReviewThread> ReviewThreads { get; set; } = [];

    public string? ThreadsError { get; set; }

    /// <summary>Held open to keep threads pending while a test looks at the diff without them (#186).</summary>
    public Task ThreadsGate { get; set; } = Task.CompletedTask;

    /// <summary>The user's viewed state for each of the PR's files, by path, as GitHub would report it (#396).</summary>
    public Dictionary<string, GitHubViewedState> ViewedFiles { get; set; } = [];

    public async Task<GitHubResult<GitHubReviewState>> GetReviewStateAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        await ThreadsGate;
        if (Missing) return GitHubResult<GitHubReviewState>.NoCli();
        return ThreadsError is { } error
            ? GitHubResult<GitHubReviewState>.Failure(error)
            : GitHubResult<GitHubReviewState>.Success(new GitHubReviewState(ReviewThreads, new Dictionary<string, GitHubViewedState>(ViewedFiles)));
    }

    public string? ViewedError { get; set; }

    /// <summary>The marks <see cref="SetViewedAsync"/> was asked to make, in order.</summary>
    public List<(string PullRequestId, string Path, bool Viewed)> ViewedChanges { get; } = [];

    /// <summary>Paths <see cref="SetViewedAsync"/> refuses with <see cref="ViewedError"/>; every path while this is empty.</summary>
    public HashSet<string> ViewedRefused { get; } = [];

    public Task<GitHubResult<bool>> SetViewedAsync(
        string repoRoot, string pullRequestId, string path, bool viewed, CancellationToken cancellationToken = default)
    {
        lock (ViewedChanges)
        {
            ViewedChanges.Add((pullRequestId, path, viewed));
            if (ViewedError is { } error && (ViewedRefused.Count == 0 || ViewedRefused.Contains(path)))
                return Task.FromResult(GitHubResult<bool>.Failure(error));
            ViewedFiles[path] = viewed ? GitHubViewedState.Viewed : GitHubViewedState.Unviewed;
            return Task.FromResult(GitHubResult<bool>.Success(true));
        }
    }

    public Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default)
    {
        Checkouts.Add((worktreePath, number));
        return Task.FromResult(CheckoutError is { } error
            ? GitHubResult<bool>.Failure(error)
            : GitHubResult<bool>.Success(true));
    }
}
