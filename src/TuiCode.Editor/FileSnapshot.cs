using TuiCode.Abstractions;

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
    /// The file's content now if it really differs from this snapshot, else null. A file that has gone
    /// is not a difference to take up: there's nothing left to read, and nothing to reload.
    /// </summary>
    public string? ReadIfChanged(IFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Look(file).Content;
    }

    /// <summary>Where the file on disk stands against this snapshot.</summary>
    public DiskState StateOf(IFileInfo file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Look(file).State;
    }

    /// <summary>
    /// mtime and length are the cheap screen; only a real content difference counts, so a <c>git checkout</c>
    /// that rewrites the file byte for byte is not a change. Read once, so the caller that wants the new
    /// text and the one that only wants to know share a single pass over the file.
    /// </summary>
    private (DiskState State, string? Content) Look(IFileInfo file)
    {
        file.Refresh();
        if (!file.Exists) return (DiskState.Gone, null);
        if (file.LastWriteTimeUtc == LastWriteTimeUtc && file.Length == Length) return (DiskState.Unchanged, null);
        var content = file.FileSystem.File.ReadAllText(file.FullName);
        return string.Equals(content, Content, StringComparison.Ordinal)
            ? (DiskState.Unchanged, null)
            : (DiskState.Changed, content);
    }
}
