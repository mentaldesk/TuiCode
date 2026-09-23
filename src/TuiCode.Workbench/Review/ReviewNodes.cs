using TuiCode.Abstractions;

namespace TuiCode.Workbench.Review;

/// <summary>A row in the Review tab: a folder holding the changed files directly in it.</summary>
public abstract class ReviewNode
{
    public List<ReviewNode> Children { get; } = [];
}

public sealed class ReviewFolderNode(string path) : ReviewNode
{
    public string Path { get; } = path;
    public override string ToString() => Path;
}

public sealed class ReviewFileNode(GitChange change, IReadOnlyList<GitHubReviewThread>? threads = null) : ReviewNode
{
    public GitChange Change { get; } = change;

    /// <summary>The review threads on this file, outdated ones included (#186).</summary>
    public IReadOnlyList<GitHubReviewThread> Threads { get; } = threads ?? [];

    public int UnresolvedCount => Threads.Count(t => !t.Resolved);

    /// <summary>The threads the badge counts: the ones still open, or all of them once they're settled.</summary>
    public int BadgeCount => UnresolvedCount > 0 ? UnresolvedCount : Threads.Count;

    /// <summary>The mark after the name, with a circle standing in for the chat icon wherever none is drawn (#186).</summary>
    public string? Badge(bool icon) => Threads.Count == 0 ? null
        : icon ? $"{BadgeCount}"
        : UnresolvedCount > 0 ? $"● {BadgeCount}"
        : $"○ {BadgeCount}";

    public TextStyle BadgeStyle => UnresolvedCount > 0 ? TextStyle.Bold : TextStyle.Faint;

    public string Name => Change.Path[(Change.Path.LastIndexOf('/') + 1)..];

    public char Mark => Change.Kind switch
    {
        GitChangeKind.Added => 'A',
        GitChangeKind.Deleted => 'D',
        GitChangeKind.Renamed => 'R',
        _ => 'M',
    };

    public override string ToString() => $"{Mark} {Name}";
}

internal static class ReviewRow
{
    /// <summary>A row as the tree shows it: its text, with a file's thread badge after it.</summary>
    public static string Display(ReviewNode node, bool icon = false)
    {
        var text = node.ToString() ?? string.Empty;
        return node is ReviewFileNode file && file.Badge(icon) is { } badge ? $"{text}  {badge}" : text;
    }
}

/// <summary>The threads on a file whose line is gone (#186), read in a tab of their own on Enter.</summary>
public sealed class ReviewOutdatedNode : ReviewNode
{
    public ReviewOutdatedNode(GitChange change, IReadOnlyList<GitHubReviewThread> threads)
    {
        Change = change;
        Threads = threads;
        // One row each as well as the tab, so a thread with no line left is still something cc can be aimed at (#189).
        foreach (var thread in threads) Children.Add(new ReviewThreadNode(this, thread));
    }

    public GitChange Change { get; }

    public IReadOnlyList<GitHubReviewThread> Threads { get; }

    public override string ToString() => Threads.Count == 1 ? "! 1 outdated thread" : $"! {Threads.Count} outdated threads";
}

/// <summary>One outdated thread, so <c>cc</c> can reply to it from the Review tab (#189).</summary>
public sealed class ReviewThreadNode(ReviewOutdatedNode outdated, GitHubReviewThread thread) : ReviewNode
{
    /// <summary>The file's outdated threads, which Enter still opens to read them all in one tab.</summary>
    public ReviewOutdatedNode Outdated { get; } = outdated;

    public GitHubReviewThread Thread { get; } = thread;

    public override string ToString() => $"{Thread.First?.Author}: {FirstLine(Thread.First?.Body)}";

    private static string FirstLine(string? body) =>
        (body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n').FirstOrDefault(line => line.Length > 0) ?? string.Empty;
}

internal static class ReviewTree
{
    /// <summary>One node per folder, by its full path, then the files at the repo root; each sorted ordinally.</summary>
    public static IReadOnlyList<ReviewNode> Build(IReadOnlyList<GitChange> changes, IReadOnlyList<GitHubReviewThread>? threads = null)
    {
        var byPath = (threads ?? [])
            .GroupBy(t => t.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var files = changes.Select(c =>
        {
            var on = byPath.GetValueOrDefault(c.Path) ?? [];
            var file = new ReviewFileNode(c, on);
            if (on.Where(t => t.Outdated).ToList() is { Count: > 0 } outdated)
                file.Children.Add(new ReviewOutdatedNode(c, outdated));
            return file;
        }).ToList();
        var folders = files
            .Where(f => f.Change.Path.Contains('/'))
            .GroupBy(f => f.Change.Path[..f.Change.Path.LastIndexOf('/')], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var folder = new ReviewFolderNode(g.Key);
                folder.Children.AddRange(g.OrderBy(f => f.Name, StringComparer.Ordinal));
                return (ReviewNode)folder;
            });
        return [.. folders, .. files.Where(f => !f.Change.Path.Contains('/')).OrderBy(f => f.Name, StringComparer.Ordinal)];
    }
}
