using TuiCode.Abstractions;
using TuiCode.Editor;

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
    /// <summary>The PR's review threads (#186): empty until they've loaded, and without a PR there are none.</summary>
    public IReadOnlyList<GitHubReviewThread> Threads { get; init; } = [];

    /// <summary>The lines each file adds and removes, by path (#393): empty until they've loaded, or when git couldn't count them.</summary>
    public IReadOnlyDictionary<string, GitLineCount> LineCounts { get; init; } = new Dictionary<string, GitLineCount>();

    /// <summary>The user's viewed state for each of the PR's files, by path (#396): null until it's loaded, and without a PR.</summary>
    public IReadOnlyDictionary<string, GitHubViewedState>? Viewed { get; init; }

    public bool IsViewed(string path) => Viewed?.GetValueOrDefault(path) == GitHubViewedState.Viewed;

    /// <summary>What the foot of the tab says about viewed files, e.g. <c>Viewed 4 of 12</c>; empty until they're known.</summary>
    public string ViewedLine => Viewed is null ? string.Empty : $"Viewed {Changes.Count(c => IsViewed(c.Path))} of {Changes.Count}";

    /// <summary>
    /// The places in <paramref name="files"/> of the files not yet viewed, starting after <paramref name="index"/>
    /// and wrapping round to the ones before it (#398).
    /// </summary>
    public IEnumerable<int> UnviewedAfter(IReadOnlyList<GitChange> files, int index) =>
        Enumerable.Range(1, Math.Max(files.Count - 1, 0))
            .Select(step => (index + step) % files.Count)
            .Where(at => !IsViewed(files[at].Path));

    /// <summary>A review diff's place in the status bar, e.g. <c>File 3 of 7  •  Viewed</c> (#398).</summary>
    public static string? SpotLabel(ReviewSpot? spot, BranchReview? review) =>
        spot is null ? null
        : review is not null && review.MergeBase == spot.Key && review.IsViewed(spot.Path) ? $"{spot.Label}  •  Viewed"
        : spot.Label;

    /// <summary>The changed files under <paramref name="folder"/>, its subfolders' included (#399).</summary>
    public IReadOnlyList<string> FilesUnder(string folder) =>
        [.. Changes.Select(c => c.Path).Where(path => path.StartsWith($"{folder}/", StringComparison.Ordinal))];

    /// <summary>The same review with <paramref name="path"/> marked viewed or not.</summary>
    public BranchReview WithViewed(string path, bool viewed) => WithViewed([path], viewed);

    /// <summary>The same review with each of <paramref name="paths"/> marked viewed or not.</summary>
    public BranchReview WithViewed(IEnumerable<string> paths, bool viewed)
    {
        var marks = new Dictionary<string, GitHubViewedState>(Viewed ?? new Dictionary<string, GitHubViewedState>(), StringComparer.Ordinal);
        foreach (var path in paths) marks[path] = viewed ? GitHubViewedState.Viewed : GitHubViewedState.Unviewed;
        return this with { Viewed = marks };
    }

    /// <summary>The threads on one file, in the order GitHub listed them.</summary>
    public IReadOnlyList<GitHubReviewThread> ThreadsOn(string path) =>
        [.. Threads.Where(t => string.Equals(t.Path, path, StringComparison.Ordinal))];

    /// <summary>What the header says about the threads, e.g. <c>3 threads, 1 unresolved</c>; empty when there are none.</summary>
    public string ThreadsLine
    {
        get
        {
            if (Threads.Count == 0) return string.Empty;
            var unresolved = Threads.Count(t => !t.Resolved);
            var threads = Threads.Count == 1 ? "1 thread" : $"{Threads.Count} threads";
            return unresolved == 0 ? threads : $"{threads}, {unresolved} unresolved";
        }
    }

    /// <summary>The size of the change, e.g. <c>5 files  +214 −38</c> (#394); just the file count until the lines are counted.</summary>
    public string TotalsLine
    {
        get
        {
            if (Changes.Count == 0) return string.Empty;
            var files = Changes.Count == 1 ? "1 file" : $"{Changes.Count} files";
            if (LineCounts.Count == 0) return files;
            return $"{files}  +{LineCounts.Values.Sum(c => c.Added)} −{LineCounts.Values.Sum(c => c.Deleted)}";
        }
    }

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
                LineCounts = new Dictionary<string, GitLineCount>(),
                PullRequest = pullRequest,
            });
        }
        return GitHubResult<BranchReview?>.Failure($"Nothing here for #{pullRequest.Number}'s base, {pullRequest.BaseBranch}: fetch it to review against it");
    }
}
