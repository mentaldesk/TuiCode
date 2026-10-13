using TuiCode.Abstractions;

namespace TuiCode.Editor;

/// <summary>Where a diff sits in a file's history (#534), so next/previous change can step from commit to commit.</summary>
/// <param name="Path">The full path of the file whose history it is.</param>
public sealed record HistorySpot(string Path, string RepoRoot, IReadOnlyList<GitFileCommit> Commits, int Index)
{
    public GitFileCommit Commit => Commits[Index];

    public string Label => $"Commit {Index + 1} of {Commits.Count}";
}
