using TuiCode.Abstractions;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Usages;

/// <summary>A row in the Usages tab: a file, a usage in it, or what a lookup under a usage is doing.</summary>
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

/// <summary>What a usage sits in: <paramref name="Name"/> as shown, <paramref name="Word"/> as written, and where that is.</summary>
public sealed record UsageDefinition(string Name, string Word, SourceLocation At);

public sealed class UsageLineNode(SourceLocation location, string number, string line, UsageDefinition? definition, bool labelled) : UsageNode
{
    public SourceLocation Location { get; } = location;

    public UsageDefinition? Definition { get; } = definition;

    public UsageLineNode? Above { get; internal set; }

    public bool Loops { get; internal set; }

    public UsageLookup? Lookup { get; internal set; }

    public bool CanFollow => Definition is not null && !Loops;

    public string Text => labelled ? $"{number}  {Label}  {line}" : $"{number}  {line}";

    private string Label => Definition is { } d ? $"{(Loops ? "↺ " : "")}in {d.Name}" : "(top level)";

    public override string ToString() => Text;
}

public sealed class UsageNoteNode(Func<string> text) : UsageNode
{
    public override string ToString() => text();
}

public static class UsageTree
{
    /// <summary>The usages by file, in path then line order; <paramref name="lines"/> is asked once per file.</summary>
    public static IReadOnlyList<UsageFileNode> Build(
        IEnumerable<SourceLocation> usages, Func<string, IReadOnlyList<string>> lines, string? root,
        Func<SourceLocation, UsageDefinition?>? definitionOf = null)
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
                file.Children.Add(new UsageLineNode(usage, (usage.Line + 1).ToString().PadLeft(width), line, definitionOf?.Invoke(usage), definitionOf is not null));
            }
            files.Add(file);
        }
        return files;
    }

    /// <summary>One usage per line, leaving out each line that defines <paramref name="name"/>.</summary>
    public static IReadOnlyList<SourceLocation> TextUsages(
        IEnumerable<(string Path, int Row, int Column)> matches, string name, Func<string, FileDefinitions> definitions) =>
        [.. matches
            .GroupBy(m => (m.Path, m.Row))
            .Select(g => g.MinBy(m => m.Column))
            .Where(m => !definitions(m.Path).Defines(name, m.Row))
            .Select(m => new SourceLocation(m.Path, m.Row, m.Column))];

    public static string Count(IReadOnlyList<UsageFileNode> files) =>
        $"{files.Sum(f => f.Count)} in {files.Count} {Plural.Of(files.Count, "file")}";

    private static string DisplayPath(string path, string? root) =>
        root is not null && FilePaths.IsSameOrUnder(path, root) ? System.IO.Path.GetRelativePath(root, path) : path;
}
