using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>What the current branch changes against the merge base of <c>HEAD</c> with <see cref="Base"/>.</summary>
public sealed record BranchReview(string RepoRoot, string Branch, string Base, string MergeBase, IReadOnlyList<GitChange> Changes)
{
    public string Header => $"{Branch} ← {Base}  (no PR)";

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
}
