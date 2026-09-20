using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

/// <summary>The rows of the <c>opr</c> picker: filtering, and how each open PR reads.</summary>
internal static class PullRequestList
{
    private const int Gap = 2;

    /// <summary>The marker on a PR whose review is requested of you, explained under the list.</summary>
    public const string RequestedMarker = "●";

    /// <summary>Case-insensitive substring of the number, the title or the author. A leading <c>#</c> is ignored.</summary>
    public static IReadOnlyList<GitHubPullRequestSummary> Filter(IReadOnlyList<GitHubPullRequestSummary> entries, string filter)
    {
        filter = filter.Trim().TrimStart('#');
        return filter.Length == 0 ? entries : entries.Where(e => Matches(e, filter)).ToList();
    }

    public static string Display(GitHubPullRequestSummary pullRequest, int width)
    {
        var right = pullRequest.ReviewRequested ? $"{pullRequest.Author} {RequestedMarker}" : $"{pullRequest.Author}  ";
        var left = $"#{pullRequest.Number}  {pullRequest.Title}";
        var leftWidth = Math.Max(0, width - right.Length - Gap);
        return Truncate(left, leftWidth).PadRight(leftWidth + Gap) + right;
    }

    private static bool Matches(GitHubPullRequestSummary pullRequest, string filter) =>
        pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal)
        || pullRequest.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || pullRequest.Author.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
