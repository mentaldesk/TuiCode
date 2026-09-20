using System.Globalization;
using System.Text;
using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>
/// The Overview tab's text (#185): a PR's description and conversation as one Markdown document,
/// which <see cref="TuiCode.Editor.DocumentTab"/> renders.
/// </summary>
public static class PullRequestOverview
{
    private const string Rule = "---";
    private const string NoDescription = "*No description*";

    /// <summary>The tab's title, and the name of the document behind it.</summary>
    public static string TitleOf(int number) => $"#{number} Overview";

    public static string Build(GitHubConversation conversation)
    {
        // LF whatever the OS: this is a buffer to read, not a file written back to disk.
        var text = new StringBuilder()
            .Append("# #").Append(conversation.Number).Append(' ').Append(conversation.Title).Append('\n')
            .Append('\n').Append(Heading(conversation.Author, conversation.Date)).Append('\n')
            .Append('\n').Append(Body(conversation.Body)).Append('\n');
        foreach (var comment in conversation.Comments)
            text.Append('\n').Append(Rule).Append('\n')
                .Append('\n').Append(Heading(comment.Author, comment.Date)).Append('\n')
                .Append('\n').Append(Body(comment.Body)).Append('\n');
        return text.ToString();
    }

    private static string Heading(string author, DateTimeOffset date) =>
        $"{author} · {date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    // GitHub hands back whatever the poster's editor used, CRLF and all; the buffer models lines itself.
    private static string Body(string body) =>
        body.Trim().Replace("\r\n", "\n").Replace('\r', '\n') is { Length: > 0 } text ? text : NoDescription;
}
