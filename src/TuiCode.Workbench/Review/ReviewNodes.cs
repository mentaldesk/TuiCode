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

public sealed class ReviewFileNode(GitChange change) : ReviewNode
{
    public GitChange Change { get; } = change;

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

internal static class ReviewTree
{
    /// <summary>One node per folder, by its full path, then the files at the repo root; each sorted ordinally.</summary>
    public static IReadOnlyList<ReviewNode> Build(IReadOnlyList<GitChange> changes)
    {
        var files = changes.Select(c => new ReviewFileNode(c)).ToList();
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
