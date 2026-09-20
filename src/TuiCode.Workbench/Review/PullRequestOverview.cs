using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The Overview tab's text (#185): a PR's description and conversation as Markdown, which is what
/// colours it. Nothing is rendered — the source is what a reviewer reads on GitHub anyway.
/// </summary>
public static class PullRequestOverview
{
    private const string Rule = "---";
    private const string NoDescription = "*No description*";

    /// <summary>The tab's title, and the name of the document behind it.</summary>
    public static string TitleOf(int number) => $"#{number} Overview";

    /// <summary>The tab's title for the threads on a file whose lines are gone (#186).</summary>
    public static string OutdatedTitleOf(int number, string path) => $"#{number} Outdated: {path}";

    /// <summary>The same reading as the Overview, for threads that have no line left to sit on (#186).</summary>
    public static string BuildOutdated(int number, string path, IReadOnlyList<GitHubReviewThread> threads)
    {
        var text = new StringBuilder()
            .Append("# #").Append(number).Append(" outdated threads on ").Append(path).Append('\n');
        foreach (var thread in threads)
        {
            text.Append('\n').Append(Rule).Append('\n');
            foreach (var comment in thread.Comments)
                text.Append('\n').Append(comment.Heading).Append('\n')
                    .Append('\n').Append(Body(comment.Body)).Append('\n');
        }
        return text.ToString();
    }

    public static string Build(GitHubConversation conversation)
    {
        // LF whatever the OS: this is a buffer to read, not a file written back to disk.
        var text = new StringBuilder()
            .Append("# #").Append(conversation.Number).Append(' ').Append(conversation.Title).Append('\n')
            .Append('\n').Append(GitHubComment.HeadingFor(conversation.Author, conversation.Date)).Append('\n')
            .Append('\n').Append(Body(conversation.Body)).Append('\n');
        foreach (var comment in conversation.Comments)
            text.Append('\n').Append(Rule).Append('\n')
                .Append('\n').Append(comment.Heading).Append('\n')
                .Append('\n').Append(Body(comment.Body)).Append('\n');
        return text.ToString();
    }

    // GitHub hands back whatever the poster's editor used, CRLF and all; the buffer models lines itself.
    private static string Body(string body) =>
        body.Trim().Replace("\r\n", "\n").Replace('\r', '\n') is { Length: > 0 } text ? text : NoDescription;
}
