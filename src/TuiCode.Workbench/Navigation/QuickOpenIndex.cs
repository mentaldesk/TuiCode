using TuiCode.Search;

namespace TuiCode.Workbench.Navigation;

internal sealed record QuickOpenEntry(OpenEntryKind Kind, string RelativePath, IFileSystemInfo Info)
{
    public string Display => Kind == OpenEntryKind.Directory ? RelativePath + "/" : RelativePath;
}

internal sealed record QuickOpenIndex(IReadOnlyList<QuickOpenEntry> Entries, bool Truncated)
{
    public const int DefaultMaxEntries = 10_000;

    /// <summary>Breadth-first, so a capped scan keeps the shallowest entries.</summary>
    public static QuickOpenIndex Scan(IDirectoryInfo root, int maxEntries = DefaultMaxEntries, CancellationToken cancellationToken = default)
    {
        var entries = new List<QuickOpenEntry>();
        var pending = new Queue<(IDirectoryInfo Dir, string Prefix)>();
        pending.Enqueue((root, ""));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, prefix) = pending.Dequeue();

            IDirectoryInfo[] subdirs;
            IFileInfo[] files;
            try
            {
                subdirs = dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                files = dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var d in subdirs.Where(d => !WorkspaceSearch.ExcludedDirectories.Contains(d.Name)))
            {
                if (entries.Count == maxEntries) return new QuickOpenIndex(entries, Truncated: true);
                entries.Add(new QuickOpenEntry(OpenEntryKind.Directory, prefix + d.Name, d));
                pending.Enqueue((d, prefix + d.Name + "/"));
            }
            foreach (var f in files)
            {
                if (entries.Count == maxEntries) return new QuickOpenIndex(entries, Truncated: true);
                entries.Add(new QuickOpenEntry(OpenEntryKind.File, prefix + f.Name, f));
            }
        }
        return new QuickOpenIndex(entries, Truncated: false);
    }

    /// <summary>A query with a <c>/</c> matches path segments, otherwise just the name.</summary>
    public IReadOnlyList<QuickOpenEntry> Filter(string query)
    {
        var byPath = query.Contains('/');
        return Entries
            .Select(e => (Entry: e, Score: byPath ? CamelHumps.ScorePath(query, e.RelativePath.Split('/')) : CamelHumps.Score(query, e.Info.Name)))
            .Where(m => m.Score is not null)
            .OrderBy(m => m.Score)
            .ThenBy(m => m.Entry.Info.Name.Length)
            .ThenBy(m => m.Entry.RelativePath.Count(c => c == '/'))
            .ThenBy(m => m.Entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(m => m.Entry)
            .ToList();
    }
}
