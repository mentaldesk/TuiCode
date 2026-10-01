namespace TuiCode.Workbench.Workspace;

/// <summary>A row of the <c>or</c> picker: the folder, its name, and its parent with the home directory as <c>~</c>.</summary>
public sealed record RecentFolder(string Path, string Name, string Parent);

/// <summary>The rows of the <c>or</c> picker: which remembered folders to offer, filtering, and how each reads.</summary>
internal static class RecentFolderList
{
    private const int Gap = 2;

    /// <summary>The remembered folders in their order, less the one open now and any that are gone.</summary>
    public static IReadOnlyList<RecentFolder> Rows(IEnumerable<string> folders, string? current, Func<string, bool> exists, string home) =>
        [.. folders
            .Where(f => !SamePath(f, current) && exists(f))
            .Select(f => Row(f, home))];

    public static RecentFolder Row(string folder, string home)
    {
        var path = TrimEnd(folder);
        var cut = path.LastIndexOfAny(Separators);
        if (cut < 0 || cut == path.Length - 1) return new RecentFolder(folder, path, "");
        var parent = cut == 0 || path[cut - 1] == ':' ? path[..(cut + 1)] : path[..cut];
        return new RecentFolder(folder, path[(cut + 1)..], ShortenHome(parent, TrimEnd(home)));
    }

    /// <summary>Case-insensitive substring of the name or the parent.</summary>
    public static IReadOnlyList<RecentFolder> Filter(IReadOnlyList<RecentFolder> rows, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0
            ? rows
            : [.. rows.Where(r => r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.Parent.Contains(filter, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>A filter containing <c>/</c> or starting with <c>~</c> is a folder to open rather than a filter.</summary>
    public static bool IsPath(string filter)
    {
        filter = filter.Trim();
        return filter.Contains('/') || filter.StartsWith('~');
    }

    /// <summary>The typed path with a leading <c>~</c> as <paramref name="home"/> and no trailing separator.</summary>
    public static string ExpandPath(string filter, string home)
    {
        var path = filter.Trim();
        if (home.Length > 0 && (path == "~" || (path.Length > 1 && path[0] == '~' && Separators.Contains(path[1]))))
            path = TrimEnd(home) + path[1..];
        return TrimEnd(path);
    }

    /// <summary>The name in a column <paramref name="nameWidth"/> wide, then the parent.</summary>
    public static string Display(RecentFolder row, int nameWidth, int width)
    {
        nameWidth = Math.Min(nameWidth, Math.Max(0, width / 2 - Gap));
        var line = Truncate(row.Name, nameWidth).PadRight(nameWidth + Gap) + row.Parent;
        return Truncate(line, width);
    }

    private static readonly char[] Separators = ['/', '\\'];

    private static string ShortenHome(string parent, string home)
    {
        if (home.Length == 0) return parent;
        if (parent == home) return "~";
        return parent.StartsWith(home, StringComparison.Ordinal) && Separators.Contains(parent[home.Length])
            ? "~" + parent[home.Length..]
            : parent;
    }

    private static bool SamePath(string a, string? b) => b is not null && TrimEnd(a) == TrimEnd(b);

    private static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed[^1] == ':' ? path : trimmed;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
