using TuiCode.Abstractions;

namespace TuiCode.Workbench.Git;

/// <summary>The rows of the <c>opr</c> picker: how each open PR reads.</summary>
internal static class PullRequestList
{
    private const int Gap = 2;

    /// <summary>The marker on a PR whose review is requested of you, explained beside the buttons.</summary>
    public const string RequestedMarker = "●";

    public static string Display(GitHubPullRequestSummary pullRequest, int width)
    {
        var right = pullRequest.ReviewRequested ? $"{pullRequest.Author} {RequestedMarker}" : $"{pullRequest.Author}  ";
        var left = $"#{pullRequest.Number}  {pullRequest.Title}";
        var leftWidth = Math.Max(0, width - right.Length - Gap);
        return Truncate(left, leftWidth).PadRight(leftWidth + Gap) + right;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : max <= 1 ? s[..max] : s[..(max - 1)] + "…";
}
