namespace TuiCode.Search;

/// <summary>A row in the search results tree: directory → file → match, mirroring the explorer.</summary>
public abstract class SearchNode
{
    public List<SearchNode> Children { get; } = [];
}

public sealed class DirectoryNode(string name) : SearchNode
{
    public string Name { get; } = name;
    public override string ToString() => Name;
}

public sealed class FileNode(FileSearchResult result) : SearchNode
{
    public FileSearchResult Result { get; } = result;
    public override string ToString() => $"{Result.File.Name} ({Result.Matches.Count})";
}

public sealed class MatchNode(IFileInfo file, LineMatch match) : SearchNode
{
    private const int PreviewLeadChars = 12;
    private const int PreviewMaxChars = 80;

    private string? _display;

    public IFileInfo File { get; } = file;
    public LineMatch Line { get; } = match;

    // Cached: TG measures every row whenever the tree changes.
    public override string ToString() => _display ??= $"{Line.Match.Row + 1}: {Preview(Line.LineText, Line.Match)}";

    /// <summary>The text around <paramref name="match"/>, cut to a short row so a minified line stays cheap to show.</summary>
    internal static string Preview(string text, TextMatch match)
    {
        var leading = 0;
        while (leading < text.Length && char.IsWhiteSpace(text[leading])) leading++;
        var trailing = text.Length;
        while (trailing > leading && char.IsWhiteSpace(text[trailing - 1])) trailing--;

        var start = Math.Max(leading, match.Column - PreviewLeadChars);
        var end = Math.Min(trailing, Math.Max(start + PreviewMaxChars, match.Column + match.Length));
        if (start > leading && char.IsLowSurrogate(text[start])) start--;
        if (end < trailing && char.IsLowSurrogate(text[end])) end++;

        var preview = text[start..end].Replace('\t', ' ');
        return $"{(start > leading ? "…" : "")}{preview}{(end < trailing ? "…" : "")}";
    }
}

internal static class SearchResultTree
{
    /// <summary>Group results into top-level nodes by their directory path relative to <paramref name="root"/>.</summary>
    public static IReadOnlyList<SearchNode> Build(IDirectoryInfo root, WorkspaceSearchResult result)
    {
        var fs = root.FileSystem;
        var top = new List<SearchNode>();
        var dirs = new Dictionary<string, DirectoryNode>(StringComparer.Ordinal);

        foreach (var file in result.Files)
        {
            var fileNode = new FileNode(file);
            fileNode.Children.AddRange(file.Matches.Select(m => new MatchNode(file.File, m)));

            var relativeDir = fs.Path.GetRelativePath(root.FullName, file.File.DirectoryName ?? root.FullName);
            var siblings = top;
            if (relativeDir is not ("." or ""))
            {
                var path = "";
                foreach (var segment in relativeDir.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
                {
                    path = path.Length == 0 ? segment : path + "/" + segment;
                    if (!dirs.TryGetValue(path, out var dir))
                    {
                        dir = new DirectoryNode(segment);
                        dirs[path] = dir;
                        siblings.Add(dir);
                    }
                    siblings = dir.Children;
                }
            }
            siblings.Add(fileNode);
        }
        return top;
    }
}
