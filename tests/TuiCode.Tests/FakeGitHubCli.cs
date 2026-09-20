using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeGitHubCli : IGitHubCli
{
    public GitHubPullRequest? PullRequest { get; set; }
    public string? Error { get; set; }
    public bool Missing { get; set; }
    public int Calls { get; private set; }

    public GitHubConversation? Conversation { get; set; }

    public string? ConversationError { get; set; }

    /// <summary>The PR numbers <see cref="GetConversationAsync"/> was asked about, in order.</summary>
    public List<int> ConversationCalls { get; } = [];

    public Task<GitHubResult<GitHubConversation>> GetConversationAsync(string repoRoot, int number, CancellationToken cancellationToken = default)
    {
        ConversationCalls.Add(number);
        if (Missing) return Task.FromResult(GitHubResult<GitHubConversation>.NoCli());
        return Task.FromResult(ConversationError is { } error
            ? GitHubResult<GitHubConversation>.Failure(error)
            : GitHubResult<GitHubConversation>.Success(
                Conversation ?? new GitHubConversation(number, $"#{number}", "octocat", default, string.Empty, [])));
    }

    public Task<GitHubResult<GitHubPullRequest?>> GetPullRequestAsync(string repoRoot, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Missing) return Task.FromResult(GitHubResult<GitHubPullRequest?>.NoCli());
        return Task.FromResult(Error is { } error
            ? GitHubResult<GitHubPullRequest?>.Failure(error)
            : GitHubResult<GitHubPullRequest?>.Success(PullRequest));
    }
}
