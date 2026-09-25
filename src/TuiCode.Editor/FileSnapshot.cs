namespace TuiCode.Editor;

/// <summary>
/// A file as an editor last saw it — on load, and again after every save (#267). Lets a save tell
/// its own last write from someone else's before it overwrites anything. Later slices of #133 ask
/// the same question for markers and reloads.
/// </summary>
public sealed record FileSnapshot(DateTime LastWriteTimeUtc, long Length, string Content)
{
    /// <summary>What <paramref name="file"/> looks like now, given the <paramref name="content"/> just read or written.</summary>
    public static FileSnapshot Of(IFileInfo file, string content)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        return file.Exists
            ? new FileSnapshot(file.LastWriteTimeUtc, file.Length, content)
            : new FileSnapshot(default, -1, content);
    }

    /// <summary>
    /// Whether the file on disk now differs from this snapshot. mtime and length are the cheap
    /// screen; only a real content difference counts, so a <c>git checkout</c> that rewrites the
    /// file byte for byte is not a change. A file that has gone is not one either — there's
    /// nothing left to overwrite.
    /// </summary>
    public bool DiffersOnDisk(IFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) return false;
        if (file.LastWriteTimeUtc == LastWriteTimeUtc && file.Length == Length) return false;
        return !string.Equals(file.FileSystem.File.ReadAllText(file.FullName), Content, StringComparison.Ordinal);
    }
}
