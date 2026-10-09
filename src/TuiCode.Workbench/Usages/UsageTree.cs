using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Usages;

/// <summary>A row in the Usages tab: a file, or a usage in it.</summary>
public abstract class UsageNode
{
    public List<UsageNode> Children { get; } = [];
}

public sealed class UsageFileNode(string path, string name) : UsageNode
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public int Count => Children.Count;
    public override string ToString() => Name;
}

public sealed class UsageLineNode(SourceLocation location, string text) : UsageNode
{
    public SourceLocation Location { get; } = location;
    public string Text { get; } = text;
    public override string ToString() => Text;
}

public static class UsageTree
{
    /// <summary>The usages by file, in path then line order; <paramref name="lines"/> is asked once per file.</summary>
    public static IReadOnlyList<UsageFileNode> Build(IEnumerable<SourceLocation> usages, Func<string, IReadOnlyList<string>> lines, string? root)
    {
        var byFile = usages
            .GroupBy(u => u.Path, StringComparer.Ordinal)
            .OrderBy(g => DisplayPath(g.Key, root), StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (Path: g.Key, Usages: g.OrderBy(u => u.Line).ThenBy(u => u.Character).ToList()))
            .ToList();
        var width = byFile.SelectMany(f => f.Usages).Select(u => (u.Line + 1).ToString().Length).DefaultIfEmpty(1).Max();
        var names = byFile.GroupBy(f => System.IO.Path.GetFileName(f.Path), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var files = new List<UsageFileNode>();
        foreach (var (path, fileUsages) in byFile)
        {
            var name = System.IO.Path.GetFileName(path);
            var file = new UsageFileNode(path, names[name] > 1 ? DisplayPath(path, root) : name);
            var text = lines(path);
            foreach (var usage in fileUsages)
            {
                var line = usage.Line < text.Count ? text[usage.Line].Trim().Replace('\t', ' ') : "";
                file.Children.Add(new UsageLineNode(usage, $"{(usage.Line + 1).ToString().PadLeft(width)}  {line}"));
            }
            files.Add(file);
        }
        return files;
    }

    public static string Count(IReadOnlyList<UsageFileNode> files) =>
        $"{files.Sum(f => f.Count)} in {files.Count} {(files.Count == 1 ? "file" : "files")}";

    private static string DisplayPath(string path, string? root) =>
        root is not null && FilePaths.IsSameOrUnder(path, root) ? System.IO.Path.GetRelativePath(root, path) : path;
}
