using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

/// <summary>A row of the <c>ow</c> picker: the worktree's folder, its branch, its name, and its parent with the home directory as <c>~</c>.</summary>
public sealed record WorktreeRow(string Path, string Branch, string Name, string Parent);

/// <summary>The rows of the <c>ow</c> picker: which worktrees to offer, filtering, and how each reads.</summary>
internal static class WorktreeList
{
    private const int Gap = 2;

    private static readonly char[] Separators = ['/', '\\'];

    public static readonly WorktreeRow Headings = new("", "Branch", "Worktree", "Location");

    /// <summary>Every worktree in git's order, less the one at <paramref name="current"/>.</summary>
    public static IReadOnlyList<WorktreeRow> Rows(IEnumerable<GitWorktree> worktrees, string current, string home) =>
        [.. worktrees.Where(w => TrimEnd(w.Path) != TrimEnd(current)).Select(w => Row(w, home))];

    public static WorktreeRow Row(GitWorktree worktree, string home)
    {
        var branch = worktree.Branch ?? $"(detached {worktree.ShortHead})";
        var path = TrimEnd(worktree.Path);
        var cut = path.LastIndexOfAny(Separators);
        if (cut < 0 || cut == path.Length - 1) return new WorktreeRow(worktree.Path, branch, path, "");
        var parent = cut == 0 || path[cut - 1] == ':' ? path[..(cut + 1)] : path[..cut];
        return new WorktreeRow(worktree.Path, branch, path[(cut + 1)..], ShortenHome(parent, TrimEnd(home)));
    }

    /// <summary>Case-insensitive substring of the branch, the name or the parent.</summary>
    public static IReadOnlyList<WorktreeRow> Filter(IReadOnlyList<WorktreeRow> rows, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0
            ? rows
            : [.. rows.Where(r => r.Branch.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || r.Parent.Contains(filter, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>The branch and the name in columns as wide as their longest, each capped at a third of the row, then the parent.</summary>
    public static string Display(WorktreeRow row, int branchWidth, int nameWidth, int width)
    {
        var cap = Math.Max(0, width / 3 - Gap);
        branchWidth = Math.Min(branchWidth, cap);
        nameWidth = Math.Min(nameWidth, cap);
        var line = Truncate(row.Branch, branchWidth).PadRight(branchWidth + Gap)
            + Truncate(row.Name, nameWidth).PadRight(nameWidth + Gap)
            + row.Parent;
        return Truncate(line, width);
    }

    public static string Header(int branchWidth, int nameWidth, int width) => Display(Headings, branchWidth, nameWidth, width);

    private static string ShortenHome(string parent, string home)
    {
        if (home.Length == 0) return parent;
        if (parent == home) return "~";
        return parent.StartsWith(home, StringComparison.Ordinal) && Separators.Contains(parent[home.Length])
            ? "~" + parent[home.Length..]
            : parent;
    }

    private static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed[^1] == ':' ? path : trimmed;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
