namespace TuiCode.Workbench.Assistant;

/// <summary>The lines a question is about (#566): the selection, or the lines around the cursor. Lines are 0-based.</summary>
public sealed record AskContext(string Path, string RelativePath, int First, int Last, IReadOnlyList<string> Lines)
{
    /// <summary>How far either side of the cursor the context reaches with nothing selected.</summary>
    public const int CursorWindow = 40;

    /// <summary><c>lines 41–58 of src/X.cs</c>, 1-based.</summary>
    public string Label => First == Last ? $"line {First + 1} of {RelativePath}" : $"lines {First + 1}–{Last + 1} of {RelativePath}";

    public string Text => string.Join('\n', Lines);

    /// <summary>The file's URI with the lines as a fragment, which the chat reads back when a conversation is reloaded.</summary>
    public string Uri => $"{Languages.LspLocations.ToUri(Path)}#L{First + 1}-{Last + 1}";

    public static AskContext Of(string path, string? root, IReadOnlyList<string> lines, (int First, int Last)? selected, int cursorRow)
    {
        var last = Math.Max(lines.Count - 1, 0);
        var (first, end) = selected ?? (cursorRow - CursorWindow, cursorRow + CursorWindow);
        first = Math.Clamp(first, 0, last);
        end = Math.Clamp(end, first, last);
        var relative = root is null ? System.IO.Path.GetFileName(path) : System.IO.Path.GetRelativePath(root, path);
        if (relative.StartsWith("..", StringComparison.Ordinal)) relative = path;
        return new AskContext(path, relative.Replace('\\', '/'), first, end, [.. lines.Skip(first).Take(end - first + 1)]);
    }

    /// <summary>The label for a URI <see cref="Uri"/> made, relative to <paramref name="root"/>; null for any other.</summary>
    public static string? LabelOf(string uri, string? root)
    {
        var hash = uri.LastIndexOf("#L", StringComparison.Ordinal);
        if (hash < 0 || !uri.StartsWith("file:", StringComparison.Ordinal)) return null;
        var range = uri[(hash + 2)..].Split('-');
        if (range.Length != 2 || !int.TryParse(range[0], out var first) || !int.TryParse(range[1], out var last)) return null;
        string path;
        try
        {
            path = new System.Uri(uri[..hash]).LocalPath;
        }
        catch (UriFormatException)
        {
            return null;
        }
        return Of(path, root, [.. Enumerable.Repeat("", last)], (first - 1, last - 1), 0).Label;
    }
}
