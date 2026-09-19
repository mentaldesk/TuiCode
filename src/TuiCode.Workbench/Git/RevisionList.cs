using TuiCode.Abstractions;
using TuiCode.Workbench.Navigation;

namespace TuiCode.Workbench.Git;

/// <summary>A row of the <c>ctr</c> picker. <see cref="Revision"/> is what git is asked for; the diff tab is titled with it too.</summary>
internal sealed record RevisionEntry(string Revision, string Text, string Kind, string? Subject = null)
{
    private const int Gap = 2;

    public string Display(int width)
    {
        var textWidth = Math.Max(0, width - Kind.Length - Gap);
        return Truncate(Text, textWidth).PadRight(textWidth + Gap) + Kind;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}

internal static class RevisionList
{
    private static readonly RevisionEntry Head = new("HEAD", "HEAD", "checked out");

    /// <summary><c>HEAD</c>, refs in the order <see cref="IGitCli.GetRefsAsync"/> gives them, then commits, newest first.</summary>
    public static IReadOnlyList<RevisionEntry> Build(IReadOnlyList<GitRef> refs, IReadOnlyList<GitCommit> commits) =>
    [
        Head,
        .. refs.Select(r => new RevisionEntry(r.Name, r.Name, r.Kind == GitRefKind.Tag ? "tag" : "branch")),
        .. commits.Select(c => new RevisionEntry(c.ShortHash, $"{c.ShortHash}  {c.Subject.Replace('\t', ' ')}", "commit", c.Subject)),
    ];

    /// <summary>Branches and tags by <see cref="CamelHumps"/>, commits by hash prefix or subject. The order stays as built.</summary>
    public static IReadOnlyList<RevisionEntry> Filter(IReadOnlyList<RevisionEntry> entries, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0 ? entries : entries.Where(e => Matches(e, filter)).ToList();
    }

    /// <summary>The first row <paramref name="filter"/> names exactly (<c>HEAD</c> in any case), else the first row.</summary>
    public static int? Selected(IReadOnlyList<RevisionEntry> visible, string filter)
    {
        if (visible.Count == 0) return null;
        filter = filter.Trim();
        for (var i = 0; i < visible.Count; i++)
        {
            var named = visible[i] == Head
                ? filter.Equals(Head.Revision, StringComparison.OrdinalIgnoreCase)
                : visible[i].Revision == filter;
            if (named) return i;
        }
        return 0;
    }

    private static bool Matches(RevisionEntry entry, string filter) =>
        entry.Subject is null
            ? CamelHumps.Score(filter, entry.Revision) is not null
            : entry.Revision.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
              || entry.Subject.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
