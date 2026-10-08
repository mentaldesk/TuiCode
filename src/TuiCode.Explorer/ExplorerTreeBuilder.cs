namespace TuiCode.Explorer;

// TG's builder lists a folder to tell whether it can expand, and TG asks that of every row each time the tree changes (#450).
internal sealed class ExplorerTreeBuilder : ITreeBuilder<IFileSystemInfo>
{
    private readonly FileSystemTreeBuilder _entries = new() { IncludeFiles = true };

    public bool SupportsCanExpand => true;

    public bool CanExpand(IFileSystemInfo entry) =>
        entry is IDirectoryInfo && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    public IEnumerable<IFileSystemInfo> GetChildren(IFileSystemInfo entry) => _entries.GetChildren(entry);
}
