namespace TuiCode.Explorer;

public sealed class FileExplorerView : TreeView<IFileSystemInfo>
{
    public event EventHandler<IFileInfo>? FileActivated;

    /// <summary>The directory the tree is currently rooted at, or null before the first <see cref="Open"/>.</summary>
    public IDirectoryInfo? Root { get; private set; }

    public FileExplorerView()
    {
        TreeBuilder = new FileSystemTreeBuilder { IncludeFiles = true };
        AspectGetter = info => info.Name;
        Activated += (_, _) => ActivateSelected();

        // TG TreeView's default Enter binding maps to Command.Activate but does NOT raise the
        // Activated event (Space does). Intercept Enter at the KeyDown level so users can open
        // files with either key — confirmed by FileExplorerViewTests.Enter_key_activates_the_selected_file.
        KeyDown += (_, key) =>
        {
            if (key == Key.Enter)
            {
                ActivateSelected();
                key.Handled = true;
            }
        };
    }

    public void Open(IDirectoryInfo root)
    {
        Root = root;
        ClearObjects();
        AddObject(root);
        Expand(root);
    }

    public void ActivateSelected()
    {
        if (SelectedObject is IFileInfo file)
            FileActivated?.Invoke(this, file);
    }
}
