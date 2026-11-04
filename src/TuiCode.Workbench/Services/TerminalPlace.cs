namespace TuiCode.Workbench.Services;

/// <summary>What the terminal tab says TuiCode is open on: the folder's name, after its repo's when they differ.</summary>
public static class TerminalPlace
{
    public static string For(string folder, string? originUrl)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (name.Length == 0) name = folder;
        return RepoName(originUrl) is { } repo && !string.Equals(repo, name, StringComparison.OrdinalIgnoreCase)
            ? $"{repo}/{name}"
            : name;
    }

    // Covers https://host/owner/repo(.git), git@host:owner/repo.git and local paths.
    public static string? RepoName(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var last = url.TrimEnd('/', '\\').Split('/', '\\', ':')[^1];
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) last = last[..^4];
        return last.Length == 0 ? null : last;
    }
}
