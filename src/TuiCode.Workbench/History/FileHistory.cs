using Terminal.Gui.Text;
using TuiCode.Abstractions;
using TuiCode.Workbench.Git;

namespace TuiCode.Workbench.History;

/// <summary>The History panel's list (#534): which commits a filter keeps, and how each row reads.</summary>
internal static class FileHistory
{
    /// <summary>Read first, so the newest rows show while the rest load.</summary>
    public const int FirstPage = 25;

    public const int Limit = 200;

    public static readonly string CappedNote = $"Showing the newest {Limit} commits";

    /// <summary>Five rows of list under the filter, inside the border.</summary>
    public const int MinPanelHeight = 8;

    public const int EditorFloor = 10;

    private const int Gap = 2;
    private const int MaxAuthorWidth = 24;

    /// <summary>
    /// A third of the <paramref name="available"/> rows, leaving the editor <see cref="EditorFloor"/>, but never below
    /// <see cref="MinPanelHeight"/>: on a terminal too short for both, the panel's floor wins.
    /// </summary>
    public static int PanelHeight(int available) =>
        Math.Max(MinPanelHeight, Math.Min(available / 3, available - EditorFloor));

    /// <summary>Commits whose hash starts with, or whose author or subject contains, <paramref name="filter"/>, in any case.</summary>
    public static IReadOnlyList<GitFileCommit> Filter(IReadOnlyList<GitFileCommit> commits, string filter)
    {
        filter = filter.Trim();
        return filter.Length == 0
            ? commits
            : [.. commits.Where(c => c.Hash.StartsWith(filter, StringComparison.OrdinalIgnoreCase)
                                     || c.Author.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                     || c.Subject.Contains(filter, StringComparison.OrdinalIgnoreCase))];
    }

    public static string Age(GitFileCommit commit, DateTimeOffset now) => BlameView.Ago(now - commit.Date);

    /// <summary>The age and author columns' widths, as wide as the widest of <paramref name="commits"/>.</summary>
    public static (int Age, int Author) Columns(IReadOnlyList<GitFileCommit> commits, DateTimeOffset now) =>
        commits.Count == 0
            ? (0, 0)
            : (commits.Max(c => Age(c, now).GetColumns()), Math.Min(MaxAuthorWidth, commits.Max(c => c.Author.GetColumns())));

    /// <summary>Hash, age, author and subject in columns, cut at <paramref name="width"/>.</summary>
    public static string Row(GitFileCommit commit, (int Age, int Author) columns, DateTimeOffset now, int width)
    {
        var gap = new string(' ', Gap);
        var row = string.Concat(
            commit.ShortHash, gap,
            Pad(Age(commit, now), columns.Age), gap,
            Pad(Cut(commit.Author, columns.Author), columns.Author), gap,
            commit.Subject.Replace('\t', ' '));
        return Cut(row, width);
    }

    private static string Pad(string text, int columns) => text + new string(' ', Math.Max(0, columns - text.GetColumns()));

    private static string Cut(string text, int columns)
    {
        if (text.GetColumns() <= columns) return text;
        if (columns <= 0) return "";
        var kept = "";
        foreach (var element in text.EnumerateTextElements())
        {
            if ((kept + element).GetColumns() > columns - 1) break;
            kept += element;
        }
        return kept + "…";
    }

    private static IEnumerable<string> EnumerateTextElements(this string text)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }
}
