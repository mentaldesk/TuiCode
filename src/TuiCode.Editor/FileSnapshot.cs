namespace TuiCode.Editor;

/// <summary>
/// A file as an editor last saw it — on load, on reload, and again after every save (#267). Lets a save
/// tell its own last write from someone else's before it overwrites anything, and a reload (#269) tell
/// what's on disk from what the tab is already showing.
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
    /// The file's content now if it really differs from this snapshot, else null. mtime and length are
    /// the cheap screen; only a real content difference counts, so a <c>git checkout</c> that rewrites
    /// the file byte for byte is not a change. A file that has gone is not one either — there's nothing
    /// left to overwrite, and nothing to reload.
    /// </summary>
    public string? ReadIfChanged(IFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        file.Refresh();
        if (!file.Exists) return null;
        if (file.LastWriteTimeUtc == LastWriteTimeUtc && file.Length == Length) return null;
        var content = file.FileSystem.File.ReadAllText(file.FullName);
        return string.Equals(content, Content, StringComparison.Ordinal) ? null : content;
    }

    /// <summary>Whether the file on disk now differs from this snapshot.</summary>
    public bool DiffersOnDisk(IFileInfo file) => ReadIfChanged(file) is not null;
}
