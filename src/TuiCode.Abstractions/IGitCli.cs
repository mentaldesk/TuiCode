namespace TuiCode.Abstractions;

/// <summary>
/// Read-only queries against the <c>git</c> CLI. Every call returns a <see cref="GitResult{T}"/>:
/// a missing <c>git</c>, a non-zero exit or a timeout is a failure with a message fit for the
/// status bar, never an exception.
/// </summary>
public interface IGitCli
{
    /// <summary>The root of the repo containing <paramref name="path"/>, or null when it isn't in one.</summary>
    Task<GitResult<string?>> GetRepoRootAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The file's content at <paramref name="revision"/>, or null when the file isn't in that revision.</summary>
    Task<GitResult<string?>> ShowFileAsync(string filePath, string revision, CancellationToken cancellationToken = default);

    /// <summary>Local branches, remote branches and tags of the repo containing <paramref name="path"/>, in that order.</summary>
    Task<GitResult<IReadOnlyList<GitRef>>> GetRefsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The newest commits that touched <paramref name="filePath"/>, newest first.</summary>
    Task<GitResult<IReadOnlyList<GitCommit>>> GetFileHistoryAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Whether <paramref name="revision"/> resolves to a commit in the repo containing <paramref name="path"/>.</summary>
    Task<GitResult<bool>> ResolvesAsync(string path, string revision, CancellationToken cancellationToken = default);

    /// <summary>The branch checked out in the repo containing <paramref name="path"/>, or null when <c>HEAD</c> is detached.</summary>
    Task<GitResult<string?>> GetCurrentBranchAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The branch <c>origin/HEAD</c> points at, else <c>main</c>, else <c>master</c>; null when there's none of them.</summary>
    Task<GitResult<string?>> GetDefaultBranchAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The full hash of the best common ancestor of two revisions, or null when they share no history.</summary>
    Task<GitResult<string?>> GetMergeBaseAsync(string path, string first, string second, CancellationToken cancellationToken = default);

    /// <summary>Files that differ between <paramref name="revision"/> and the working copy, staged or not, with repo-relative paths.</summary>
    Task<GitResult<IReadOnlyList<GitChange>>> GetChangedFilesAsync(string path, string revision, CancellationToken cancellationToken = default);

    /// <summary>The content of <paramref name="repoPath"/> (relative to <paramref name="repoRoot"/>) at <paramref name="revision"/>, or null when it isn't there.</summary>
    Task<GitResult<string?>> ShowRepoFileAsync(string repoRoot, string repoPath, string revision, CancellationToken cancellationToken = default);
}

public readonly record struct GitResult<T>(T Value, string? Error)
{
    public bool Succeeded => Error is null;

    public static GitResult<T> Success(T value) => new(value, null);

    public static GitResult<T> Failure(string error) => new(default!, error);
}

public enum GitRefKind
{
    Branch,
    RemoteBranch,
    Tag,
}

public sealed record GitRef(string Name, GitRefKind Kind);

public sealed record GitCommit(string ShortHash, string Subject, DateTimeOffset Date);

public enum GitChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

/// <summary>A changed file. Paths are relative to the repo root, with <c>/</c> separators; <see cref="OldPath"/> is set for renames.</summary>
public sealed record GitChange(GitChangeKind Kind, string Path, string? OldPath = null);
