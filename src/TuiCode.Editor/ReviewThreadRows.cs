using TuiCode.Abstractions;

namespace TuiCode.Editor;

/// <summary>One row of a review thread in the diff: <see cref="Trailing"/> is put at the right of the row.</summary>
public readonly record struct ThreadRow(string Text, string? Trailing = null);

/// <summary>
/// How a review thread reads in a diff tab (#186): one row while it's collapsed, every comment once
/// it's expanded. Long lines aren't wrapped — the row is cut like any other.
/// </summary>
public static class ReviewThreadRows
{
    private const string Gutter = "┃";

    public static IReadOnlyList<ThreadRow> For(GitHubReviewThread thread, bool expanded)
    {
        if (!expanded)
        {
            var first = thread.First;
            return [new ThreadRow($"{Gutter} {first?.Author}: {FirstLine(first?.Body)}", Replies(thread.Replies))];
        }

        var rows = new List<ThreadRow>();
        foreach (var comment in thread.Comments)
        {
            if (rows.Count > 0) rows.Add(new ThreadRow(Gutter));
            rows.Add(new ThreadRow($"{Gutter} {comment.Heading}"));
            foreach (var line in Lines(comment.Body))
                rows.Add(new ThreadRow(line.Length == 0 ? Gutter : $"{Gutter} {line}"));
        }
        return rows;
    }

    private static string? Replies(int replies) => replies switch
    {
        0 => null,
        1 => "(1 reply)",
        _ => $"({replies} replies)",
    };

    private static string FirstLine(string? body) => Lines(body).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

    private static string[] Lines(string? body) =>
        (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').Split('\n');
}
