namespace TuiCode.Editor;

/// <summary>
/// The file a diff side shows, and the commit it's at with the file's repo-relative path there; no
/// <see cref="Revision"/> for a buffer or a file on disk.
/// </summary>
public sealed record DiffSideOrigin(string FilePath, string? Revision = null, string? RepoPath = null);
