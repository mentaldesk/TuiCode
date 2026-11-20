using System.Text;
using TuiCode.Editor;
using TuiCode.Workbench.Languages;

namespace TuiCode.Workbench.Rename;

/// <summary>A server's rename, split between the files open in tabs and the files only on disk.</summary>
public sealed class WorkspaceRename
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly IReadOnlyDictionary<string, string> _openText;
    private readonly Dictionary<string, byte[]> _closedBytes;

    private WorkspaceRename(IReadOnlyList<FileEdit> open, IReadOnlyList<FileEdit> closed,
        IReadOnlyDictionary<string, string> openText, Dictionary<string, byte[]> closedBytes)
    {
        Open = open;
        Closed = closed;
        _openText = openText;
        _closedBytes = closedBytes;
    }

    public IReadOnlyList<FileEdit> Open { get; }
    public IReadOnlyList<FileEdit> Closed { get; }
    public int Changes => Open.Concat(Closed).Sum(f => f.Edits.Count);
    public int Files => Open.Count + Closed.Count;

    /// <param name="openText">Each open tab's lines joined with <c>\n</c>, as they were when the server was asked.</param>
    public static WorkspaceRename Plan(IReadOnlyList<FileEdit> files, IReadOnlyDictionary<string, string> openText, IFileSystem fileSystem)
    {
        List<FileEdit> open = [], closed = [];
        var bytes = new Dictionary<string, byte[]>(LspLocations.PathComparer);
        foreach (var file in files)
        {
            if (openText.ContainsKey(file.Path))
                open.Add(file);
            else if (fileSystem.File.Exists(file.Path))
            {
                closed.Add(file);
                bytes[file.Path] = fileSystem.File.ReadAllBytes(file.Path);
            }
        }
        return new WorkspaceRename(open, closed, openText, bytes);
    }

    /// <summary>Makes the edits: each open tab's as one undo step, left unsaved, and each closed file's written with its BOM and line endings.</summary>
    public RenameResult Apply(IEnumerable<EditorTab> tabs, IFileSystem fileSystem)
    {
        List<FileEdit> applied = [];
        List<string> skipped = [];
        var byPath = tabs.GroupBy(t => t.File.FullName, LspLocations.PathComparer).ToDictionary(g => g.Key, g => g.First(), LspLocations.PathComparer);
        foreach (var file in Open)
        {
            if (byPath.TryGetValue(file.Path, out var tab) && ApplyTo(tab, file)) applied.Add(file);
            else skipped.Add(file.Path);
        }
        foreach (var file in Closed)
        {
            if (byPath.ContainsKey(file.Path) || !ApplyTo(fileSystem, file)) skipped.Add(file.Path);
            else applied.Add(file);
        }
        return new RenameResult(applied, skipped);
    }

    private bool ApplyTo(EditorTab tab, FileEdit file)
    {
        var old = string.Join('\n', tab.Lines);
        if (tab.ReadOnly || old != _openText[file.Path] || TextEdits.Apply(old, file.Edits) is not { } renamed) return false;
        var before = old.Split('\n');
        var after = renamed.Split('\n');
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        tab.ReplaceLines(prefix, before.Length - prefix - suffix, after[prefix..^suffix]);
        return true;
    }

    // A file that changed since the server answered, or isn't UTF-8, is left as it is.
    private bool ApplyTo(IFileSystem fileSystem, FileEdit file)
    {
        try
        {
            var bytes = fileSystem.File.ReadAllBytes(file.Path);
            if (!bytes.AsSpan().SequenceEqual(_closedBytes[file.Path])) return false;
            var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
            var text = StrictUtf8.GetString(bytes, hasBom ? Utf8Bom.Length : 0, bytes.Length - (hasBom ? Utf8Bom.Length : 0));
            if (TextEdits.Apply(text, file.Edits) is not { } renamed) return false;
            var written = StrictUtf8.GetBytes(renamed);
            fileSystem.File.WriteAllBytes(file.Path, hasBom ? [.. Utf8Bom, .. written] : written);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException or EncoderFallbackException)
        {
            return false;
        }
    }
}

/// <summary>The files a rename changed, and those it left alone because they'd changed or couldn't be written.</summary>
public sealed record RenameResult(IReadOnlyList<FileEdit> Applied, IReadOnlyList<string> Skipped)
{
    public int Changes => Applied.Sum(f => f.Edits.Count);

    /// <summary>One place per changed line, where its first change now starts.</summary>
    public IEnumerable<SourceLocation> ChangedLines() =>
        Applied.SelectMany(f => TextEdits.NewStarts(f.Edits)
            .GroupBy(p => p.Line).Select(g => g.MinBy(p => p.Character))
            .Select(p => new SourceLocation(f.Path, p.Line, p.Character)));
}
