using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>What the current branch changes against the merge base of <c>HEAD</c> with <see cref="Base"/>.</summary>
public sealed record BranchReview(
    string RepoRoot,
    string Branch,
    string Base,
    string MergeBase,
    IReadOnlyList<GitChange> Changes,
    GitHubPullRequest? PullRequest = null)
{
    public string Header => PullRequest is { } pr
        ? $"{pr.BaseBranch} ← {pr.HeadBranch}"
        : $"{Branch} ← {Base}  (no PR)";

    public string TitleLine => PullRequest is { } pr ? $"#{pr.Number} {pr.Title}" : string.Empty;

    public string ChecksLine
    {
        get
        {
            if (PullRequest is not { Checks: { Total: > 0 } checks }) return string.Empty;
            var counts = new List<string>(3);
            if (checks.Passed > 0) counts.Add($"✓ {checks.Passed}");
            if (checks.Failed > 0) counts.Add($"✗ {checks.Failed}");
            if (checks.Pending > 0) counts.Add($"● {checks.Pending}");
            return $"{string.Join("  ", counts)} checks";
        }
    }

    /// <summary>Cuts <paramref name="text"/> to <paramref name="width"/> cells, ending it with an ellipsis. A width of 0 or less doesn't cut.</summary>
    public static string Truncate(string text, int width) =>
        width <= 0 || text.Length <= width ? text : string.Concat(text.AsSpan(0, Math.Max(width - 1, 0)), "…");

    /// <summary>Null, without an error, when <paramref name="folder"/> isn't in a git repo.</summary>
    public static async Task<GitResult<BranchReview?>> LoadAsync(IGitCli git, string folder, CancellationToken cancellationToken = default)
    {
        var root = await git.GetRepoRootAsync(folder, cancellationToken);
        if (root.Error is { } rootError) return GitResult<BranchReview?>.Failure(rootError);
        if (root.Value is not { } repoRoot) return GitResult<BranchReview?>.Success(null);

        var branch = await git.GetCurrentBranchAsync(repoRoot, cancellationToken);
        if (branch.Error is { } branchError) return GitResult<BranchReview?>.Failure(branchError);

        var defaultBranch = await git.GetDefaultBranchAsync(repoRoot, cancellationToken);
        if (defaultBranch.Error is { } defaultError) return GitResult<BranchReview?>.Failure(defaultError);
        if (defaultBranch.Value is not { } baseBranch) return GitResult<BranchReview?>.Failure("No default branch: no origin/HEAD, main or master");

        var mergeBase = await git.GetMergeBaseAsync(repoRoot, "HEAD", baseBranch, cancellationToken);
        if (mergeBase.Error is { } mergeBaseError) return GitResult<BranchReview?>.Failure(mergeBaseError);
        if (mergeBase.Value is not { } baseCommit) return GitResult<BranchReview?>.Failure($"No history in common with {baseBranch}");

        var changes = await git.GetChangedFilesAsync(repoRoot, baseCommit, cancellationToken);
        if (changes.Error is { } changesError) return GitResult<BranchReview?>.Failure(changesError);

        return GitResult<BranchReview?>.Success(new BranchReview(repoRoot, branch.Value ?? "HEAD", baseBranch, baseCommit, changes.Value));
    }

    /// <summary>
    /// The same review against the branch's open PR — its base, so the files and diffs match what
    /// GitHub shows. Null, without an error, when the branch has no PR.
    /// </summary>
    public static async Task<GitHubResult<BranchReview?>> WithPullRequestAsync(
        IGitCli git, IGitHubCli gitHub, BranchReview review, CancellationToken cancellationToken = default)
    {
        var found = await gitHub.GetPullRequestAsync(review.RepoRoot, cancellationToken);
        if (!found.Succeeded) return new GitHubResult<BranchReview?>(null, found.Error, found.CliUnavailable);
        if (found.Value is not { } pullRequest) return GitHubResult<BranchReview?>.Success(null);

        // The default branch git picked is usually the PR's base already, under its remote name.
        if (review.Base == pullRequest.BaseBranch || review.Base == $"origin/{pullRequest.BaseBranch}")
            return GitHubResult<BranchReview?>.Success(review with { PullRequest = pullRequest });

        foreach (var candidate in (string[])[$"origin/{pullRequest.BaseBranch}", pullRequest.BaseBranch])
        {
            var resolves = await git.ResolvesAsync(review.RepoRoot, candidate, cancellationToken);
            if (resolves.Error is { } resolveError) return GitHubResult<BranchReview?>.Failure(resolveError);
            if (!resolves.Value) continue;

            var mergeBase = await git.GetMergeBaseAsync(review.RepoRoot, "HEAD", candidate, cancellationToken);
            if (mergeBase.Error is { } mergeBaseError) return GitHubResult<BranchReview?>.Failure(mergeBaseError);
            if (mergeBase.Value is not { } baseCommit) return GitHubResult<BranchReview?>.Failure($"No history in common with {candidate}");

            var changes = await git.GetChangedFilesAsync(review.RepoRoot, baseCommit, cancellationToken);
            if (changes.Error is { } changesError) return GitHubResult<BranchReview?>.Failure(changesError);

            return GitHubResult<BranchReview?>.Success(review with
            {
                Base = candidate,
                MergeBase = baseCommit,
                Changes = changes.Value,
                PullRequest = pullRequest,
            });
        }
        return GitHubResult<BranchReview?>.Failure($"Nothing here for #{pullRequest.Number}'s base, {pullRequest.BaseBranch}: fetch it to review against it");
    }
}
