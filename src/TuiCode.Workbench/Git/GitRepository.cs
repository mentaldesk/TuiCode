namespace TuiCode.Workbench.Git;

/// <summary>Whether a folder sits inside a git repository, read from the filesystem.</summary>
internal static class GitRepository
{
    /// <summary>
    /// Walks up for <c>.git</c> — a directory, or the file a worktree or submodule has instead. Command
    /// enablement is read while the palette builds its rows, which <see cref="GitCli"/> can't answer in time.
    /// </summary>
    public static bool Contains(IDirectoryInfo? folder)
    {
        for (var dir = folder; dir is not null; dir = dir.Parent)
        {
            var dotGit = dir.FileSystem.Path.Combine(dir.FullName, ".git");
            if (dir.FileSystem.Directory.Exists(dotGit) || dir.FileSystem.File.Exists(dotGit)) return true;
        }
        return false;
    }
}
