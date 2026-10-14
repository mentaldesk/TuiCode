namespace TuiCode.Abstractions;

/// <summary>The OS Trash, or the Recycle Bin on Windows (#128).</summary>
public interface ITrash
{
    /// <summary>What the user calls it: <c>Trash</c>, or <c>Recycle Bin</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Move the file, folder or symlink at <paramref name="path"/> there. A symlink goes itself, never what it points to.
    /// Throws an <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> saying why it couldn't.
    /// </summary>
    void Move(string path);
}
