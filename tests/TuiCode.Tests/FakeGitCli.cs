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
        Task.FromResult(GitResult<string?>.Success(MergeBase));

    public Task<GitResult<IReadOnlyList<GitChange>>> GetChangedFilesAsync(string path, string revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(GitResult<IReadOnlyList<GitChange>>.Success(Changes));

    public Task<GitResult<string?>> ShowRepoFileAsync(string repoRoot, string repoPath, string revision, CancellationToken cancellationToken = default)
    {
        ShowCount++;
        return Task.FromResult(GitResult<string?>.Success(RepoFiles.GetValueOrDefault($"{revision}:{repoPath}")));
    }
}
