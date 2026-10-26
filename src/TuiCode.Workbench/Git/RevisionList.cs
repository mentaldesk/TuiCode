using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

/// <summary>A row of the <c>ctr</c> picker. <see cref="Revision"/> is what git is asked for; the diff tab is titled with it too.</summary>
public sealed record RevisionEntry(string Revision, string Text, string Kind, string? Subject = null)
{
    private const int Gap = 2;

    public static RevisionEntry Typed(string revision) => new(revision, revision, "");

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
}
