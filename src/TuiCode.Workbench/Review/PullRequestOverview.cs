using System.Globalization;
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

    public static string Build(GitHubConversation conversation)
    {
        var text = new StringBuilder()
            .Append("# #").Append(conversation.Number).Append(' ').AppendLine(conversation.Title)
            .AppendLine()
            .AppendLine(Heading(conversation.Author, conversation.Date))
            .AppendLine()
            .AppendLine(Body(conversation.Body));
        foreach (var comment in conversation.Comments)
            text.AppendLine()
                .AppendLine(Rule)
                .AppendLine()
                .AppendLine(Heading(comment.Author, comment.Date))
                .AppendLine()
                .AppendLine(Body(comment.Body));
        return text.ToString();
    }

    private static string Heading(string author, DateTimeOffset date) =>
        $"{author} · {date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    // GitHub hands back whatever the poster's editor used, CRLF and all; the buffer models lines itself.
    private static string Body(string body) =>
        body.Trim().Replace("\r\n", "\n").Replace('\r', '\n') is { Length: > 0 } text ? text : NoDescription;
}
