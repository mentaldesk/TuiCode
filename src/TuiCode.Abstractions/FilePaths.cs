namespace TuiCode.Abstractions;

/// <summary>Ordinal path arithmetic shared by everything that tracks files across a delete, rename or move (#101).</summary>
public static class FilePaths
{
    /// <summary>Whether <paramref name="path"/> is <paramref name="ancestor"/> itself or somewhere beneath it.</summary>
    public static bool IsSameOrUnder(string path, string ancestor)
    {
        ancestor = TrimSeparators(ancestor);
        path = TrimSeparators(path);
        if (!path.StartsWith(ancestor, StringComparison.Ordinal)) return false;
        return path.Length == ancestor.Length || IsSeparator(path[ancestor.Length]);
    }

    /// <summary>A trailing slash (either style) marks a path as a directory — how <c>Ctrl+N</c> and <c>tuicode &lt;path&gt;</c> both decide what to create.</summary>
    public static bool IsDirectoryPath(string path)
    {
        var trimmed = path.TrimEnd();
        return trimmed.EndsWith('/') || trimmed.EndsWith('\\');
    }

    /// <summary>Where <paramref name="path"/> ends up once <paramref name="from"/> moves to <paramref name="to"/>.</summary>
    public static string Rebase(string path, string from, string to)
    {
        if (!IsSameOrUnder(path, from)) return path;
        return TrimSeparators(to) + TrimSeparators(path)[TrimSeparators(from).Length..];
    }

    private static string TrimSeparators(string path) => path.Length > 1 ? path.TrimEnd('/', '\\') : path;

    private static bool IsSeparator(char c) => c is '/' or '\\';
}
