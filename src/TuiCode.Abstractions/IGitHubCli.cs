using System.Globalization;

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

    /// <summary>PR <paramref name="number"/>'s description and the comments on it, oldest first (#185).</summary>
    Task<GitHubResult<GitHubConversation>> GetConversationAsync(string repoRoot, int number, CancellationToken cancellationToken = default);

    /// <summary>The review threads on PR <paramref name="number"/> (#186), in the order GitHub lists them.</summary>
    Task<GitHubResult<IReadOnlyList<GitHubReviewThread>>> GetReviewThreadsAsync(string repoRoot, int number, CancellationToken cancellationToken = default);

    /// <summary>The repo's open PRs, newest first, each flagged when the logged-in user's review is requested.</summary>
    Task<GitHubResult<IReadOnlyList<GitHubPullRequestSummary>>> ListPullRequestsAsync(string repoRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks PR <paramref name="number"/> out in <paramref name="worktreePath"/>, fetching it first, so that
    /// <see cref="GetPullRequestAsync"/> there finds it again. True once it's checked out.
    /// </summary>
    Task<GitHubResult<bool>> CheckoutPullRequestAsync(string worktreePath, int number, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replies to the review thread <paramref name="replyToId"/> heads (#189), posted at once: GitHub's API
    /// can't hold a reply in a pending review. The reply comes back as GitHub recorded it.
    /// </summary>
    Task<GitHubResult<GitHubComment>> ReplyToThreadAsync(
        string repoRoot, int number, long replyToId, string body, CancellationToken cancellationToken = default);

    /// <summary>Submits a review on PR <paramref name="number"/>, with its draft line comments (#188). True once GitHub has it.</summary>
    Task<GitHubResult<bool>> SubmitReviewAsync(
        string repoRoot, int number, GitHubReviewVerdict verdict, string summary, IReadOnlyList<DraftComment> comments,
        CancellationToken cancellationToken = default);
}

/// <summary>What a submitted review says: the three verdicts GitHub takes.</summary>
public enum GitHubReviewVerdict
{
    Comment,
    Approve,
    RequestChanges,
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

/// <summary><c>HeadSha</c> is the commit the PR's branch is at on GitHub: what a line comment's lines have to match (#188).</summary>
public sealed record GitHubPullRequest(int Number, string Title, string BaseBranch, string HeadBranch, GitHubChecks Checks, string HeadSha = "");

/// <summary>
/// A line comment drafted on a PR (#188) and not yet posted: <c>Line</c> is the line on the head side,
/// as GitHub numbers it, and <c>Path</c> is repo-relative.
/// </summary>
public sealed record DraftComment(string Path, int Line, string Body);

/// <summary>A PR as the Overview tab reads it (#185): what it says it does, then what's been said about it.</summary>
public sealed record GitHubConversation(
    int Number,
    string Title,
    string Author,
    DateTimeOffset Date,
    string Body,
    IReadOnlyList<GitHubComment> Comments);

/// <summary>One comment on a PR, posted at <paramref name="Date"/>.</summary>
public sealed record GitHubComment(string Author, DateTimeOffset Date, string Body)
{
    /// <summary>How a comment is headed wherever it's read: the Overview tab (#185) and an expanded thread (#186).</summary>
    public string Heading => HeadingFor(Author, Date);

    /// <summary>The same heading for something that isn't a comment, like the PR's own description.</summary>
    public static string HeadingFor(string author, DateTimeOffset date) =>
        $"{author} · {date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
}

/// <summary>
/// A review thread on a PR (#186). <c>Line</c> is the line it's attached to on the head side, and is null
/// once the thread is <c>Outdated</c> — the line it was written against is no longer in the file.
/// <c>ReplyToId</c> is the comment a reply is posted under (#189); 0 when GitHub didn't give one.
/// </summary>
public sealed record GitHubReviewThread(
    string Path,
    int? Line,
    bool Resolved,
    bool Outdated,
    IReadOnlyList<GitHubComment> Comments,
    long ReplyToId = 0)
{
    /// <summary>The comment the thread is headed by; a thread always has one.</summary>
    public GitHubComment? First => Comments.Count > 0 ? Comments[0] : null;

    public int Replies => Math.Max(0, Comments.Count - 1);
}

/// <summary>A PR's checks, counted by state.</summary>
public readonly record struct GitHubChecks(int Passed, int Failed, int Pending)
{
    public int Total => Passed + Failed + Pending;
}
