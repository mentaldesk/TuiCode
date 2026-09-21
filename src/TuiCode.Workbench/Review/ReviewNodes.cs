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

    /// <summary>The mark at the right of the row: the threads still open, or all of them once they're settled.</summary>
    public string? Badge => Threads.Count == 0 ? null
        : UnresolvedCount > 0 ? $"● {UnresolvedCount}"
        : $"○ {Threads.Count}";

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
    public static string Display(ReviewNode node)
    {
        var text = node.ToString() ?? string.Empty;
        return node is ReviewFileNode { Badge: { } badge } ? $"{text}  {badge}" : text;
    }
}

/// <summary>The threads on a file whose line is gone (#186), read in a tab of their own on Enter.</summary>
public sealed class ReviewOutdatedNode(GitChange change, IReadOnlyList<GitHubReviewThread> threads) : ReviewNode
{
    public GitChange Change { get; } = change;

    public IReadOnlyList<GitHubReviewThread> Threads { get; } = threads;

    public override string ToString() => Threads.Count == 1 ? "! 1 outdated thread" : $"! {Threads.Count} outdated threads";
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
