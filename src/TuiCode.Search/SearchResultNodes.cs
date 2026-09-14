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

    public IFileInfo File { get; } = file;
    public LineMatch Line { get; } = match;

    // "12: …text around the match" — trimmed so the hit stays visible in a narrow sidebar.
    public override string ToString()
    {
        var text = Line.LineText;
        var start = Math.Max(0, Line.Match.Column - PreviewLeadChars);
        var leading = text.Length - text.TrimStart().Length;
        if (start <= leading) start = leading;
        var preview = text[start..].TrimEnd().Replace('\t', ' ');
        return $"{Line.Match.Row + 1}: {(start > leading ? "…" : "")}{preview}";
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
