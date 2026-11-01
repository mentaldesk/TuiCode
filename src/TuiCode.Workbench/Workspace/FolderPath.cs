namespace TuiCode.Workbench.Workspace;

/// <summary>A folder path as the assistant shows it.</summary>
internal static class FolderPath
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary><paramref name="path"/> with <paramref name="home"/> as <c>~</c>.</summary>
    public static string Shorten(string path, string home) => RecentFolderList.ShortenHome(TrimEnd(path), TrimEnd(home));

    private static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed[^1] == ':' ? path : trimmed;
    }
}
