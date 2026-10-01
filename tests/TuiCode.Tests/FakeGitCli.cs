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

    /// <summary>Content at <c>HEAD</c> by full path; a path with no entry isn't in <c>HEAD</c>.</summary>
    public Dictionary<string, string> HeadFiles { get; } = new(StringComparer.Ordinal);

    public string? HeadFileError { get; set; }

    /// <summary>When set, answers <see cref="ShowCheckedOutFileAsync"/> in place of <see cref="HeadFiles"/>, for the tests of reads that return late.</summary>
    public Func<string, Task<string?>>? HeadFileSource { get; set; }

    public List<string> HeadReads { get; } = [];

    public async Task<GitResult<string?>> ShowCheckedOutFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        lock (HeadReads) HeadReads.Add(filePath);
        if (HeadFileSource is { } source) return GitResult<string?>.Success(await source(filePath));
        if (Missing) return GitResult<string?>.Failure(NoGit);
        if (HeadFileError is { } error) return GitResult<string?>.Failure(error);
        return GitResult<string?>.Success(HeadFiles.GetValueOrDefault(filePath));
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

    /// <summary>The worktree path of each branch already checked out, for <see cref="FindWorktreeAsync"/>.</summary>
    public Dictionary<string, string> BranchWorktrees { get; } = new(StringComparer.Ordinal);

    public Task<GitResult<string?>> FindWorktreeAsync(string repoRoot, string branch, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<string?>.Success(BranchWorktrees.GetValueOrDefault(branch)));

    /// <summary>What <see cref="GetWorktreesAsync"/> lists, unless <see cref="ListWorktreesError"/> is set.</summary>
    public IReadOnlyList<GitWorktree> ListedWorktrees { get; set; } = [];

    public string? ListWorktreesError { get; set; }

    public Task<GitResult<IReadOnlyList<GitWorktree>>> GetWorktreesAsync(string repoRoot, CancellationToken cancellationToken = default) =>
        Task.FromResult(Missing ? GitResult<IReadOnlyList<GitWorktree>>.Failure(NoGit)
            : ListWorktreesError is { } error ? GitResult<IReadOnlyList<GitWorktree>>.Failure(error)
            : GitResult<IReadOnlyList<GitWorktree>>.Success(ListedWorktrees));

    public string? RepoFileError { get; set; }

    public Task<GitResult<string?>> ShowRepoFileAsync(string repoRoot, string repoPath, string revision, CancellationToken cancellationToken = default)
    {
        ShowCount++;
        if (RepoFileError is { } error) return Task.FromResult(GitResult<string?>.Failure(error));
        return Task.FromResult(GitResult<string?>.Success(RepoFiles.GetValueOrDefault($"{revision}:{repoPath}")));
    }

    public GitBlameLine? Blame { get; set; }
    public string? BlameError { get; set; }

    /// <summary>Each blame asked for: the line and the text passed for a dirty buffer.</summary>
    public List<(int Line, string? Contents)> Blames { get; } = [];

    public Task<GitResult<GitBlameLine?>> BlameAsync(string filePath, int line, string? contents = null, CancellationToken cancellationToken = default)
    {
        Blames.Add((line, contents));
        return Task.FromResult(BlameError is { } error ? GitResult<GitBlameLine?>.Failure(error) : GitResult<GitBlameLine?>.Success(Blame));
    }
}
