using System.Text;

namespace TuiCode.Search;

/// <summary>A match plus the text of the line it sits on, for the results tree.</summary>
public sealed record LineMatch(TextMatch Match, string LineText);

public sealed record FileSearchResult(IFileInfo File, IReadOnlyList<LineMatch> Matches);

/// <param name="NotARepo">Git couldn't list the files, so the fixed skip list stood in for what it ignores.</param>
public sealed record WorkspaceSearchResult(IReadOnlyList<FileSearchResult> Files, bool Truncated, bool NotARepo = false)
{
    public static readonly WorkspaceSearchResult Empty = new([], false);

    public int MatchCount => Files.Sum(f => f.Matches.Count);
}

/// <summary>
/// Editor buffers that are open (and possibly dirty). Workspace search reads these instead of disk,
/// and replace-all edits them in place — so unsaved edits are neither missed nor clobbered.
/// </summary>
public interface IOpenBuffers
{
    /// <summary>Content of every open buffer keyed by full path. Taken on the UI thread before a background search.</summary>
    IReadOnlyDictionary<string, string> Snapshot();

    /// <summary>Replace all matches in the open buffer for <paramref name="fullPath"/>; null if that file isn't open.</summary>
    int? ReplaceAll(string fullPath, string query, string replacement, MatchOptions options);
}

/// <summary>Pure, TG-free workspace search/replace over <see cref="IFileSystem"/>.</summary>
public static class WorkspaceSearch
{
    public const int DefaultMaxMatches = 10_000;
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int BinarySniffBytes = 8000;

    // VS Code's files.exclude / search.exclude defaults plus .NET build output, for when git can't say what it ignores.
    public static readonly IReadOnlySet<string> ExcludedDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", ".hg", ".svn", "node_modules", "bin", "obj" };

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <param name="gitFiles">The files under <paramref name="root"/> git lists, relative to it, or null to walk the folder instead.</param>
    public static WorkspaceSearchResult Search(
        IDirectoryInfo root,
        string query,
        MatchOptions options = default,
        IReadOnlyDictionary<string, string>? openBuffers = null,
        IReadOnlyList<string>? gitFiles = null,
        int maxMatches = DefaultMaxMatches,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(query)) return WorkspaceSearchResult.Empty;

        var notARepo = gitFiles is null;
        var files = new List<FileSearchResult>();
        var total = 0;
        var candidates = gitFiles is null ? EnumerateFiles(root, cancellationToken) : Listed(root, gitFiles, cancellationToken);
        foreach (var file in candidates)
        {
            string? content = null;
            if (openBuffers is not null && openBuffers.TryGetValue(file.FullName, out var buffer))
                content = buffer;
            else if (TryRead(file, out var text, out _))
                content = text;
            if (content is null) continue;

            var lines = TextSearch.SplitLines(content);
            var matches = TextSearch.FindAll(lines, query, options);
            if (matches.Count == 0) continue;

            var take = Math.Min(matches.Count, maxMatches - total);
            files.Add(new FileSearchResult(file, matches.Take(take).Select(m => new LineMatch(m, lines[m.Row])).ToArray()));
            total += take;
            if (total >= maxMatches) return new WorkspaceSearchResult(files, Truncated: true, notARepo);
        }
        return new WorkspaceSearchResult(files, Truncated: false, notARepo);
    }

    /// <summary>
    /// Replace every match in <paramref name="files"/>. Open buffers are edited in place (left dirty
    /// for the user to save); everything else is rewritten on disk, preserving line endings and a
    /// UTF-8 BOM. Returns the number of files changed and occurrences replaced.
    /// </summary>
    public static (int Files, int Occurrences) ReplaceAll(
        IEnumerable<IFileInfo> files, string query, string replacement, MatchOptions options = default, IOpenBuffers? openBuffers = null)
    {
        if (string.IsNullOrEmpty(query)) return (0, 0);

        var changedFiles = 0;
        var occurrences = 0;
        foreach (var file in files)
        {
            var count = openBuffers?.ReplaceAll(file.FullName, query, replacement, options);
            if (count is null)
            {
                if (!TryRead(file, out var text, out var hasBom)) continue;
                var replaced = TextSearch.ReplaceAll(text, query, replacement, options, out var n);
                if (n > 0)
                {
                    var bytes = new UTF8Encoding(false).GetBytes(replaced);
                    file.FileSystem.File.WriteAllBytes(file.FullName, hasBom ? [.. Utf8Bom, .. bytes] : bytes);
                }
                count = n;
            }

            if (count > 0)
            {
                changedFiles++;
                occurrences += count.Value;
            }
        }
        return (changedFiles, occurrences);
    }

    /// <summary>Files under <paramref name="root"/> in a stable order (directories then files, each ordinal-sorted).</summary>
    internal static IEnumerable<IFileInfo> EnumerateFiles(IDirectoryInfo root, CancellationToken cancellationToken)
    {
        var pending = new Stack<IDirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();

            IDirectoryInfo[] subdirs;
            IFileInfo[] files;
            try
            {
                subdirs = dir.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
                files = dir.GetFiles().OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            // Push in reverse so directories are visited in sorted order.
            for (var i = subdirs.Length - 1; i >= 0; i--)
                if (!ExcludedDirectories.Contains(subdirs[i].Name))
                    pending.Push(subdirs[i]);
        }
    }

    /// <summary>The files git listed that are still on disk, in the order <see cref="EnumerateFiles"/> walks them.</summary>
    internal static IEnumerable<IFileInfo> Listed(IDirectoryInfo root, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var fs = root.FileSystem;
        foreach (var path in paths.Distinct(StringComparer.Ordinal).Select(p => p.Split('/')).Order(WalkOrder.Instance))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = fs.FileInfo.New(fs.Path.Combine([root.FullName, .. path]));
            if (file.Exists) yield return file;
        }
    }

    // A folder's files before its subfolders, each ordinal-sorted.
    private sealed class WalkOrder : IComparer<string[]>
    {
        public static readonly WalkOrder Instance = new();

        public int Compare(string[]? x, string[]? y)
        {
            for (var i = 0; i < Math.Min(x!.Length, y!.Length); i++)
            {
                if (string.Equals(x[i], y[i], StringComparison.Ordinal)) continue;
                var xIsFile = i == x.Length - 1;
                var yIsFile = i == y.Length - 1;
                if (xIsFile != yIsFile) return xIsFile ? -1 : 1;
                return string.CompareOrdinal(x[i], y[i]);
            }
            return x.Length.CompareTo(y.Length);
        }
    }

    private static bool TryRead(IFileInfo file, out string text, out bool hasBom)
    {
        text = string.Empty;
        hasBom = false;
        try
        {
            if (file.Length > MaxFileBytes) return false;
            var bytes = file.FileSystem.File.ReadAllBytes(file.FullName);
            // A NUL in the first few KB means binary (this also skips UTF-16, which we'd mis-decode).
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinarySniffBytes)) >= 0) return false;

            hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
            text = Encoding.UTF8.GetString(bytes, hasBom ? Utf8Bom.Length : 0, bytes.Length - (hasBom ? Utf8Bom.Length : 0));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
