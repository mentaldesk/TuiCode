namespace TuiCode.Workbench.Git;

/// <summary>Whether a folder sits inside a git repository, read from the filesystem.</summary>
internal static class GitRepository
{
    /// <summary>
    /// Walks up for <c>.git</c> — a directory, or the file a worktree or submodule has instead. Command
    /// enablement is read while the palette builds its rows, which <see cref="GitCli"/> can't answer in time.
    /// </summary>
    public static bool Contains(IDirectoryInfo? folder) => DotGit(folder) is not null;

    /// <summary>Where <c>HEAD</c> and the branches live; null outside a repo or for a <c>.git</c> file we can't read.</summary>
    public static GitDirs? Dirs(IDirectoryInfo? folder)
    {
        if (DotGit(folder) is not { } dotGit) return null;
        var fs = folder!.FileSystem;
        if (fs.Directory.Exists(dotGit)) return new GitDirs(dotGit, dotGit);

        try
        {
            var line = fs.File.ReadLines(dotGit).FirstOrDefault();
            if (line is null || !line.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
            var gitDir = Resolve(fs, fs.Path.GetDirectoryName(dotGit)!, line["gitdir:".Length..].Trim());
            var commonDirFile = fs.Path.Combine(gitDir, "commondir");
            var commonDir = fs.File.Exists(commonDirFile)
                ? Resolve(fs, gitDir, fs.File.ReadAllText(commonDirFile).Trim())
                : gitDir;
            return new GitDirs(gitDir, commonDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? DotGit(IDirectoryInfo? folder)
    {
        for (var dir = folder; dir is not null; dir = dir.Parent)
        {
            var dotGit = dir.FileSystem.Path.Combine(dir.FullName, ".git");
            if (dir.FileSystem.Directory.Exists(dotGit) || dir.FileSystem.File.Exists(dotGit)) return dotGit;
        }
        return null;
    }

    private static string Resolve(IFileSystem fs, string from, string path) =>
        fs.Path.TrimEndingDirectorySeparator(fs.Path.GetFullPath(fs.Path.Combine(from, path)));
}

/// <summary>A linked worktree's own <c>HEAD</c> is in <see cref="GitDir"/>; the branches it shares are in <see cref="CommonDir"/>.</summary>
internal sealed record GitDirs(string GitDir, string CommonDir);
