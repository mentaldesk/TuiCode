using TuiCode.Abstractions;

namespace TuiCode.Tests;

internal sealed class FakeGitCli : IGitCli
{
    private const string NoGit = "git isn't installed or isn't on PATH";

    public string? Root { get; set; }
    public bool Missing { get; set; }
    public IReadOnlyList<GitRef> Refs { get; set; } = [];
    public IReadOnlyList<GitCommit> Commits { get; set; } = [];
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Resolvable { get; } = new(StringComparer.Ordinal);
    public Task RefsGate { get; set; } = Task.CompletedTask;
    public int ShowCount { get; private set; }

    public string? Branch { get; set; } = "feature";
    public string? DefaultBranch { get; set; } = "main";
    public string? MergeBase { get; set; } = "b45e";
    public IReadOnlyList<GitChange> Changes { get; set; } = [];

    /// <summary>Merge base by the revision asked about, for the tests that compare two bases; else <see cref="MergeBase"/>.</summary>
    public Dictionary<string, string> MergeBases { get; } = new(StringComparer.Ordinal);

    /// <summary>Changed files by revision, for the tests that compare two bases; else <see cref="Changes"/>.</summary>
    public Dictionary<string, IReadOnlyList<GitChange>> ChangesByRevision { get; } = new(StringComparer.Ordinal);

    /// <summary>Content by <c>revision:repoPath</c>.</summary>
    public Dictionary<string, string> RepoFiles { get; } = new(StringComparer.Ordinal);

    public Task<GitResult<string?>> GetRepoRootAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(Missing ? GitResult<string?>.Failure(NoGit) : GitResult<string?>.Success(Root));

    public Task<GitResult<string?>> ShowFileAsync(string filePath, string revision, CancellationToken cancellationToken = default)
    {
        ShowCount++;
        return Task.FromResult(GitResult<string?>.Success(Files.GetValueOrDefault(revision)));
    }

    public async Task<GitResult<IReadOnlyList<GitRef>>> GetRefsAsync(string path, CancellationToken cancellationToken = default)
    {
        await RefsGate;
        return GitResult<IReadOnlyList<GitRef>>.Success(Refs);
    }

    public Task<GitResult<IReadOnlyList<GitCommit>>> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<IReadOnlyList<GitCommit>>.Success(Commits));

    public Task<GitResult<bool>> ResolvesAsync(string path, string revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<bool>.Success(Resolvable.Contains(revision)));

    public Task<GitResult<string?>> GetCurrentBranchAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<string?>.Success(Branch));

    public Task<GitResult<string?>> GetDefaultBranchAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<string?>.Success(DefaultBranch));

    public Task<GitResult<string?>> GetMergeBaseAsync(string path, string first, string second, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<string?>.Success(MergeBases.GetValueOrDefault(second) ?? MergeBase));

    public Task<GitResult<IReadOnlyList<GitChange>>> GetChangedFilesAsync(string path, string revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<IReadOnlyList<GitChange>>.Success(ChangesByRevision.GetValueOrDefault(revision) ?? Changes));

    /// <summary>Worktree paths the fake has been asked to add, in order; a path already there isn't re-created.</summary>
    public List<string> Worktrees { get; } = [];

    public string? WorktreeError { get; set; }

    public Task<GitResult<bool>> AddWorktreeAsync(string repoRoot, string worktreePath, CancellationToken cancellationToken = default)
    {
        if (WorktreeError is { } error) return Task.FromResult(GitResult<bool>.Failure(error));
        if (Worktrees.Contains(worktreePath)) return Task.FromResult(GitResult<bool>.Success(false));
        Worktrees.Add(worktreePath);
        return Task.FromResult(GitResult<bool>.Success(true));
    }

    public Task<GitResult<string?>> ShowRepoFileAsync(string repoRoot, string repoPath, string revision, CancellationToken cancellationToken = default)
    {
        ShowCount++;
        return Task.FromResult(GitResult<string?>.Success(RepoFiles.GetValueOrDefault($"{revision}:{repoPath}")));
    }
}
