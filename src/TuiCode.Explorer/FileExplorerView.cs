namespace TuiCode.Explorer;

public sealed class FileExplorerView : TreeView<IFileSystemInfo>
{
    public event EventHandler<string>? FileActivated;

    public FileExplorerView()
    {
        TreeBuilder = new FileSystemTreeBuilder { IncludeFiles = true };
        AspectGetter = info => info.Name;
        Activated += (_, _) => ActivateSelected();
    }

    public void Open(IDirectoryInfo root)
    {
        ClearObjects();
        AddObject(root);
        Expand(root);
    }

    public void ActivateSelected()
    {
        if (SelectedObject is IFileInfo file)
            FileActivated?.Invoke(this, file.FullName);
    }
}
