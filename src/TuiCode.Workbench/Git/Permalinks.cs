using System.Text.RegularExpressions;
using TuiCode.Abstractions;
using TuiCode.Editor;

namespace TuiCode.Workbench.Git;

/// <summary>A repository on GitHub or a GitHub Enterprise host, which share a URL shape (#623).</summary>
public sealed partial record GitHubRepo(string Host, string Owner, string Name)
{
    private static readonly string[] OtherHosts = ["gitlab", "bitbucket", "dev.azure.com", "visualstudio.com"];

    /// <summary>The repo a remote URL names, in its SSH, scp-style or HTTPS form; null for a URL that isn't one, or a host that isn't GitHub.</summary>
    public static GitHubRepo? Parse(string url)
    {
        url = url.Trim();
        string host, path;
        if (!url.Contains("://", StringComparison.Ordinal) && ScpStyle().Match(url) is { Success: true } scp)
            (host, path) = (scp.Groups["host"].Value, scp.Groups["path"].Value);
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "ssh" or "git" && uri.Host.Length > 0)
            (host, path) = (uri.Host, Uri.UnescapeDataString(uri.AbsolutePath));
        else
            return null;

        if (OtherHosts.Any(other => host.Contains(other, StringComparison.OrdinalIgnoreCase))) return null;
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path.Split('/') is [{ Length: > 0 } owner, { Length: > 0 } name] ? new GitHubRepo(host.ToLowerInvariant(), owner, name) : null;
    }

    public string Blob(string commit, string repoPath, int first, int last)
    {
        var escaped = string.Join('/', repoPath.Split('/').Select(Uri.EscapeDataString));
        var lines = first == last ? $"L{first + 1}" : $"L{first + 1}-L{last + 1}";
        return $"https://{Host}/{Owner}/{Name}/blob/{commit}/{escaped}#{lines}";
    }

    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]{2,}):(?<path>[^\s]+)$")]
    private static partial Regex ScpStyle();
}

/// <summary>
/// What a permalink is to (#623): lines <paramref name="First"/>–<paramref name="Last"/> (0-based) of <paramref name="Lines"/>, the
/// text of <paramref name="FilePath"/> as a tab shows it. <paramref name="Revision"/> is the commit that text is, with the file's
/// <paramref name="RepoPath"/> there; null for a buffer or the file on disk, which are mapped back from <c>HEAD</c>.
/// </summary>
public sealed record PermalinkTarget(string FilePath, IReadOnlyList<string> Lines, int First, int Last, string? Revision = null, string? RepoPath = null);

/// <summary>A link, and what the status bar says it's to; or, with no <see cref="Url"/>, why there's none.</summary>
public sealed record Permalink(string? Url, string Message);

public static class Permalinks
{
    public static async Task<Permalink> BuildAsync(IGitCli git, IFileSystem fileSystem, PermalinkTarget target)
    {
        var name = fileSystem.Path.GetFileName(target.FilePath);
        var root = await git.GetRepoRootAsync(target.FilePath);
        if (root.Error is { } error) return new(null, error);
        if (root.Value is not { } repoRoot) return new(null, $"{name} isn't in a git repository.");

        var remote = await git.GetLinkRemoteUrlAsync(repoRoot);
        if (remote.Error is { } remoteError) return new(null, remoteError);
        if (remote.Value is not { } url) return new(null, "The repository has no remote to link to.");
        if (GitHubRepo.Parse(url) is not { } repo) return new(null, $"{url} isn't a GitHub remote.");

        var pushed = await git.GetNewestPushedCommitAsync(repoRoot, target.Revision ?? "HEAD");
        if (pushed.Error is { } pushedError) return new(null, pushedError);
        var notPushed = new Permalink(null, $"{name} isn't in any pushed commit yet.");
        if (pushed.Value is not { } commit) return notPushed;

        var repoPath = target.RepoPath ?? fileSystem.Path.GetRelativePath(repoRoot, target.FilePath).Replace('\\', '/');
        var content = await git.ShowRepoFileAsync(repoRoot, repoPath, commit.Hash);
        if (content.Error is { } contentError) return new(null, contentError);
        if (content.Value is not { } text)
        {
            if (target.Revision is not null) return notPushed;
            var head = await git.ShowRepoFileAsync(repoRoot, repoPath, "HEAD");
            return head.Value is null && head.Succeeded ? new(null, $"{name} isn't tracked by git.") : notPushed;
        }

        var (first, last, firstIsNew) = MapLines(DiffTab.SplitLines(text), target.Lines, target.First, target.Last);
        return new(repo.Blob(commit.Hash, repoPath, first, last), Describe(target, commit, first, last, firstIsNew));
    }

    /// <summary>
    /// Where lines <paramref name="first"/>–<paramref name="last"/> of <paramref name="current"/> are in <paramref name="committed"/>.
    /// A line added since has none, so it maps to the nearest earlier line that has one.
    /// </summary>
    public static (int First, int Last, bool FirstIsNew) MapLines(IReadOnlyList<string> committed, IReadOnlyList<string> current, int first, int last)
    {
        var mapped = new (int Line, bool IsNew)[current.Count];
        var previous = 0;
        var seen = 0;
        foreach (var row in AlignedDiff.Compute(committed, current, DiffTab.MaxEdits).Rows)
        {
            if (row.Left is { } left) previous = left;
            if (row.Right is not { } right) continue;
            mapped[right] = (row.Left ?? previous, row.Left is null);
            seen = right + 1;
        }
        // The diff leaves out the empty line after a final newline against an empty side.
        for (var i = seen; i < mapped.Length; i++) mapped[i] = (previous, true);

        (int Line, bool IsNew) At(int line) => mapped.Length == 0 ? (0, true) : mapped[Math.Clamp(line, 0, mapped.Length - 1)];
        var (from, isNew) = At(first);
        var to = Math.Max(from, At(last).Line);
        return (from, to, isNew);
    }

    private static string Describe(PermalinkTarget target, GitPushedCommit commit, int first, int last, bool firstIsNew)
    {
        var lines = first == last ? $"line {first + 1}" : $"lines {first + 1}–{last + 1}";
        var moved = first != target.First || last != target.Last;
        var message = $"permalink to {lines}";
        if (moved || firstIsNew || !commit.IsRevision) message += $" at {commit.ShortHash}";
        if (!commit.IsRevision) message += ", the newest pushed commit";
        if (firstIsNew) message += $" (line {target.First + 1} is new since then)";
        return message;
    }
}
