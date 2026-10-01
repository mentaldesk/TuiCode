namespace TuiCode.Workbench.Workspace;

/// <summary>A folder path typed into <c>opa</c>.</summary>
internal static class FolderPath
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>The typed path with a leading <c>~</c> as <paramref name="home"/> and no trailing separator.</summary>
    public static string Expand(string typed, string home)
    {
        var path = typed.Trim();
        if (home.Length > 0 && (path == "~" || (path.Length > 1 && path[0] == '~' && Separators.Contains(path[1]))))
            path = TrimEnd(home) + path[1..];
        return TrimEnd(path);
    }

    private static string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd(Separators);
        return trimmed.Length == 0 || trimmed[^1] == ':' ? path : trimmed;
    }
}
